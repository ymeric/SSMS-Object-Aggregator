using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using SSMS.ObjectAggregator.Models;
using System.Reflection;

namespace SSMS.ObjectAggregator.Infrastructure;

internal static class ObjectExplorerLocator
{
    #region Public API

    public static async Task<(bool Success, string? Error)> TryLocateAsync(SqlObjectReference reference, IObjectExplorerService? explorerService)
    {
        try
        {
            EnsureObjectExplorerVisible();

            if (explorerService is null)
            {
                return (false, "Object Explorer service is unavailable.");
            }

            string resolvedServerUrn = GetServerUrn(explorerService, reference.InstanceName, reference.DatabaseName);
            bool isConnected = !string.IsNullOrEmpty(resolvedServerUrn);

            if (!isConnected)
            {
                resolvedServerUrn = $"Server[@Name='{Escape(reference.InstanceName)}']";
            }

            var findNodeMethod = explorerService.GetType().GetMethod("FindNode", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            var syncMethod = explorerService.GetType().GetMethod("SynchronizeTree", BindingFlags.Public | BindingFlags.Instance);
            if (findNodeMethod is null || syncMethod is null)
            {
                return (false, "Object Explorer locate APIs are unavailable.");
            }

            if (!isConnected)
            {
                TryPromptConnection(explorerService);

                // Give Object Explorer time to register the new connection.
                await Task.Delay(1500).ConfigureAwait(true);

                string recheckedUrn = GetServerUrn(explorerService, reference.InstanceName, reference.DatabaseName);
                if (!string.IsNullOrEmpty(recheckedUrn))
                {
                    resolvedServerUrn = recheckedUrn;
                }
            }

            // FindNode only searches nodes already in the in-memory tree. Walk the hierarchy
            // top-down and trigger child loading at each level so the target node is reachable.
            // The HashSet ensures each node URN is processed (SynchronizeTree + Expand) at most
            // once across all retry attempts, preventing SSMS from restarting its metadata query.
            var expandedUrns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool isSsisPackage = string.Equals(reference.ObjectType, "SSIS_PACKAGE", StringComparison.OrdinalIgnoreCase);
            bool isSqlAgentJob = string.Equals(reference.ObjectType, "SQL_AGENT_JOB", StringComparison.OrdinalIgnoreCase);

            // For SSIS packages the Integration Services Catalog lives under the IS connection
            // root in SSMS 22, which is a separate OE root from the SQL Server connection.
            // Resolve the IS-specific root before entering the retry loop so all subsequent
            // expansion and FindNode calls use the correct server URN.
            bool isCatalogRootResolved = false;
            if (isSsisPackage)
            {
                string isCatalogRoot = FindIsCatalogServerUrn(findNodeMethod, explorerService, reference.InstanceName);
                if (!string.IsNullOrEmpty(isCatalogRoot))
                {
                    resolvedServerUrn = isCatalogRoot;
                    isCatalogRootResolved = true;
                }
            }

            TryExpandParentNodes(reference, resolvedServerUrn, findNodeMethod, explorerService, expandedUrns);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                // SSIS packages use direct TreeView traversal (no SMO queries), so a shorter
                // polling interval is sufficient — 300 ms vs. the 750 ms used for regular
                // objects that wait on asynchronous SMO metadata queries.
                await Task.Delay(isSsisPackage ? 300 : 750).ConfigureAwait(true);

                // Re-resolve the IS catalog root on early retries: the IS connection tree may
                // still be loading when the first resolution attempt runs above.
                if (!isCatalogRootResolved && isSsisPackage && attempt < 6)
                {
                    string isCatalogRoot = FindIsCatalogServerUrn(findNodeMethod, explorerService, reference.InstanceName);
                    if (!string.IsNullOrEmpty(isCatalogRoot))
                    {
                        resolvedServerUrn = isCatalogRoot;
                        isCatalogRootResolved = true;
                    }
                }

                // SQL Agent Job FindNode hangs in SSMS when @CategoryID is absent from the Job
                // URN predicate — SSMS enumerates every job synchronously on the UI thread.
                // Use TreeView traversal exclusively, the same way SSIS packages are handled.
                if (!isSqlAgentJob &&
                    TryFindAndSynchronize(reference, resolvedServerUrn, findNodeMethod, syncMethod, explorerService))
                {
                    return (true, null);
                }

                if (isSsisPackage &&
                    TryFindSsisPackageViaTreeView(reference, explorerService, syncMethod, expandedUrns))
                {
                    return (true, null);
                }

                if (isSqlAgentJob &&
                    TryFindSqlAgentJobViaTreeView(reference, explorerService, syncMethod))
                {
                    return (true, null);
                }

                // Keep nudging the parent nodes in early retries in case they are still loading.
                if (attempt < 6)
                {
                    TryExpandParentNodes(reference, resolvedServerUrn, findNodeMethod, explorerService, expandedUrns);
                }
            }

            // SSMS 22 does not expose the Integration Services Catalogs node under a standard
            // SQL Server connection. If the catalog node was never found after all retries,
            // give an actionable message and scroll OE to the SSISDB database as a fallback.
            if (isSsisPackage &&
                !expandedUrns.Contains("SSIS_TV_IS_CATALOGS"))
            {
                string ssisDbUrn = $"{resolvedServerUrn}/Database[@Name='SSISDB']";
                ExpandNode(findNodeMethod, explorerService, ssisDbUrn, expandedUrns);
                object? ssisDbNode = null;
                try { ssisDbNode = findNodeMethod.Invoke(explorerService, new object[] { ssisDbUrn }); } catch { }
                if (ssisDbNode is not null)
                {
                    try { syncMethod.Invoke(explorerService, new[] { ssisDbNode }); } catch { }
                }

                return (false,
                    $"Integration Services Catalogs is not accessible in Object Explorer.\r\n\r\n" +
                    $"No 'Integration Services' connection for '{reference.InstanceName}' was found in Object Explorer. " +
                    $"Object Explorer has been scrolled to the SSISDB database as a reference point.\r\n\r\n" +
                    $"To navigate directly to SSIS packages:\r\n" +
                    $"  1. In Object Explorer click Connect ▾ → Integration Services\r\n" +
                    $"  2. Connect to '{reference.InstanceName}'\r\n" +
                    $"  3. In SSMS 22, the 'SQL Server Integration Services Projects' feature\r\n" +
                    $"     must be installed via the SSMS installer for this option to appear.\r\n\r\n" +
                    $"Folder:\u2002\u2002{reference.SchemaName}\r\n" +
                    $"Project: {reference.ParentObjectName}\r\n" +
                    $"Package: {reference.ObjectName}");
            }

            var sampleUrns = BuildCandidateUrns(reference, resolvedServerUrn).Take(2).ToList();
            string diagInfo = CollectDiagnosticInfo(explorerService, reference.InstanceName);
            return (false, $"Could not locate '{reference.SchemaName}.{reference.ObjectName}' in Object Explorer." +
                           $"\r\n\r\nServer: {reference.InstanceName}" +
                           $"\r\nDatabase: {reference.DatabaseName}" +
                           $"\r\nURN tried: {sampleUrns.FirstOrDefault()}" +
                           $"\r\n\r\n--- DEBUG ---\r\n{diagInfo}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    #endregion Public API

    #region Utilities

    private static object? GetPropertyValue(object? obj, params string[] propertyNames)
    {
        if (obj is null) return null;
        var type = obj.GetType();

        foreach (string propName in propertyNames)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null)
            {
                return prop.GetValue(obj);
            }

            foreach (var iface in type.GetInterfaces())
            {
                prop = iface.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop is not null)
                {
                    return prop.GetValue(obj);
                }
            }
        }

        return null;
    }

    private static void EnsureObjectExplorerVisible()
    {
        var dteType = Type.GetType("EnvDTE.DTE, EnvDTE", false);
        if (dteType is null)
        {
            return;
        }

        var packageType = ResolvePackageType();

        var getGlobalService = packageType?.GetMethod("GetGlobalService", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
        object? dte = getGlobalService?.Invoke(null, new object[] { dteType });
        if (dte is null)
        {
            return;
        }

        try
        {
            dynamic dteDynamic = dte;
            dteDynamic.ExecuteCommand("View.ObjectExplorer");
        }
        catch
        {
        }
    }

    private static void TryPromptConnection(object explorerService)
    {
        var method = explorerService.GetType().GetMethod("NewConnection", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method is null)
        {
            return;
        }

        try
        {
            method.Invoke(explorerService, null);
        }
        catch
        {
        }
    }

    #endregion Utilities

    #region Server Name Utilities

    private static IEnumerable<string> GetServerNameVariations(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            yield break;
        }

        yield return rawName;
        yield return rawName.ToUpperInvariant();
        yield return rawName.ToLowerInvariant();

        if (rawName.Contains(","))
        {
            yield return rawName.Replace(",", ", ");
            yield return rawName.Replace(",", ", ").ToUpperInvariant();
            yield return rawName.Replace(",", ", ").ToLowerInvariant();
        }

        string noPort = rawName.Split(',')[0];
        if (noPort != rawName)
        {
            yield return noPort;
            yield return noPort.ToUpperInvariant();
            yield return noPort.ToLowerInvariant();
        }

        string firstPart = noPort.Split('.')[0];
        if (firstPart != noPort)
        {
            yield return firstPart;
            yield return firstPart.ToUpperInvariant();
            yield return firstPart.ToLowerInvariant();
        }

        // Sometimes the SQL instance name is returned instead of the domain name
        // Or if there is an instance name:
        if (noPort.Contains("\\"))
        {
            string[] parts = noPort.Split('\\');
            if (parts.Length > 1)
            {
                string instancePart = parts[1];
                yield return $"{firstPart}\\{instancePart}";
                yield return $"{firstPart}\\{instancePart}".ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// Extracts the port suffix from a server name that uses comma-port notation
    /// (e.g. <c>"SERVER1,12345"</c> → <c>"12345"</c>).
    /// Returns <see cref="string.Empty"/> when the name contains no comma.
    /// </summary>
    private static string ExtractPort(string? serverName)
    {
        if (string.IsNullOrEmpty(serverName)) return string.Empty;
        int idx = serverName!.IndexOf(',');
        return idx >= 0 ? serverName.Substring(idx + 1).Trim() : string.Empty;
    }

    #endregion Server Name Utilities

    #region Node Expansion and Location

    private static void TryExpandParentNodes(SqlObjectReference reference, string resolvedServerUrn, MethodInfo findNodeMethod, object explorerService, HashSet<string> expandedUrns)
    {
        bool serverWasAlreadyExpanded = expandedUrns.Contains(resolvedServerUrn);
        ExpandNode(findNodeMethod, explorerService, resolvedServerUrn, expandedUrns);

        if (IsSsisObject(reference))
        {
            TryExpandSsisParentNodes(reference, resolvedServerUrn, findNodeMethod, explorerService, expandedUrns, serverWasAlreadyExpanded);
            return;
        }

        string database = Escape(reference.DatabaseName);
        string dbUrn    = $"{resolvedServerUrn}/Database[@Name='{database}']";

        // IObjectExplorerService.FindNode returns a System.Windows.Forms.TreeNode directly.
        // Calling Expand() on that node fires the TreeView.BeforeExpand event, which is
        // SSMS's hook for issuing its SMO metadata query and populating child nodes.
        // SynchronizeTree is intentionally NOT called here — it is only needed once on the
        // final target node (in TryFindAndSynchronize) to scroll OE to the result.

        // FindNode with a URN deeper than the database level (e.g. .../StoredProcedure,
        // .../ServiceBroker) blocks synchronously while SSMS issues its SMO metadata query
        // to populate the database's children. On the first pass the database node has only
        // just been asked to expand — its children are still loading asynchronously. Calling
        // FindNode for a child URN at that point hangs the UI thread until the query finishes
        // (or deadlocks if SSMS dispatches back to the UI thread). Guard each depth level
        // with whether the parent was already expanded in a *prior* pass so that FindNode is
        // only called once the nodes it needs to traverse are already in the in-memory tree.
        bool dbWasAlreadyExpanded = expandedUrns.Contains(dbUrn);

        ExpandNode(findNodeMethod, explorerService, dbUrn, expandedUrns);

        // Skip deeper expansion on the first pass: the database node was just asked to
        // expand; its children load asynchronously. The next retry (after Task.Delay)
        // will find them already in the tree and FindNode will return immediately.
        if (!dbWasAlreadyExpanded)
            return;

        // Child objects (constraints, DML triggers) live under their parent table or view.
        if (IsChildObject(reference.ObjectType) && !string.IsNullOrEmpty(reference.ParentObjectName))
        {
            string parentSchema  = Escape(reference.ParentSchemaName ?? reference.SchemaName);
            string parentName    = Escape(reference.ParentObjectName);
            string primaryParent = reference.ParentObjectType?.Equals("VIEW", StringComparison.OrdinalIgnoreCase) == true
                ? "View" : "Table";
            string alterParent   = primaryParent == "Table" ? "View" : "Table";

            // Guard the parent-object FindNode by the same logic: only proceed to expand
            // the specific table/view node once its containing folder is already in the tree.
            bool folderWasAlreadyExpanded = expandedUrns.Contains($"{dbUrn}/{primaryParent}") ||
                                            expandedUrns.Contains($"{dbUrn}/{alterParent}");

            foreach (string folderType in new[] { primaryParent, alterParent })
                ExpandNode(findNodeMethod, explorerService, $"{dbUrn}/{folderType}", expandedUrns);

            if (folderWasAlreadyExpanded)
            {
                foreach (string parentType in new[] { primaryParent, alterParent })
                {
                    string parentUrn = $"{dbUrn}/{parentType}[@Name='{parentName}' and @Schema='{parentSchema}']";
                    if (expandedUrns.Contains(parentUrn) || ExpandNode(findNodeMethod, explorerService, parentUrn, expandedUrns))
                        break;
                }
            }

            return;
        }

        // Service Broker queues live under the ServiceBroker/ServiceQueue folder.
        if (string.Equals(reference.ObjectType, "SERVICE_QUEUE", StringComparison.OrdinalIgnoreCase))
        {
            bool sbWasAlreadyExpanded = expandedUrns.Contains($"{dbUrn}/ServiceBroker");
            ExpandNode(findNodeMethod, explorerService, $"{dbUrn}/ServiceBroker", expandedUrns);
            if (sbWasAlreadyExpanded)
                ExpandNode(findNodeMethod, explorerService, $"{dbUrn}/ServiceBroker/ServiceQueue", expandedUrns);
            return;
        }

        // Expand the type-specific collection folder so SSMS populates the individual
        // object TreeNodes that TryFindAndSynchronize will later select.
        foreach (string typeNode in GetTypeNodes(reference.ObjectType))
        {
            string folderUrn = $"{dbUrn}/{typeNode}";
            if (expandedUrns.Contains(folderUrn) || ExpandNode(findNodeMethod, explorerService, folderUrn, expandedUrns))
                break;
        }
    }

    private static bool IsSsisObject(SqlObjectReference reference) =>
        string.Equals(reference.ObjectType, "SQL_AGENT_JOB", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reference.ObjectType, "SSIS_PACKAGE",  StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Expands the SSIS-specific node hierarchy for SQL Server Agent jobs and Integration
    /// Services Catalog packages.
    /// </summary>
    private static void TryExpandSsisParentNodes(SqlObjectReference reference, string serverUrn, MethodInfo findNodeMethod, object explorerService, HashSet<string> expandedUrns, bool serverWasAlreadyExpanded)
    {
        if (string.Equals(reference.ObjectType, "SQL_AGENT_JOB", StringComparison.OrdinalIgnoreCase))
        {
            // Guard: only expand /JobServer once the server node's children have had at
            // least one retry cycle to load asynchronously. Calling FindNode on a child path
            // immediately after Expand() blocks the UI thread (same issue as database nodes).
            if (serverWasAlreadyExpanded)
                ExpandNode(findNodeMethod, explorerService, $"{serverUrn}/JobServer", expandedUrns);
            // Also drive expansion through the TreeView so the Jobs folder is populated
            // without any FindNode call that requires @CategoryID and could block.
            TryExpandSqlAgentJobsViaTreeView(reference, explorerService, expandedUrns);
            return;
        }

        if (string.Equals(reference.ObjectType, "SSIS_PACKAGE", StringComparison.OrdinalIgnoreCase))
        {
            // SSMS's FindNode API does not support IS Catalog URNs — the Integration Services
            // Catalog uses a custom OE extension provider outside the standard SMO URN registry.
            // Use direct TreeView text-traversal to expand each level of the IS hierarchy instead.
            TryExpandSsisPackageViaTreeView(reference, explorerService, expandedUrns);
        }
    }

    /// <summary>
    /// Expands the IS Catalog subtree for SSIS packages using direct TreeView node-text
    /// traversal. SSMS's <c>FindNode</c> does not work for IS Catalog URNs because the
    /// Integration Services Catalog is registered with a custom OE extension provider that is
    /// outside the standard SMO URN registry. All reachable levels are descended in a single
    /// call; the method stops naturally when <see cref="FindChildTreeNodeByText"/> returns
    /// <see langword="null"/> — meaning SSMS has not yet populated those children. The retry
    /// loop in <see cref="TryLocateAsync"/> calls this method again after a short delay, at
    /// which point the children are expected to be present.
    /// </summary>
    private static void TryExpandSsisPackageViaTreeView(
        SqlObjectReference reference, object explorerService, HashSet<string> expandedUrns)
    {
        try
        {
            // In SSMS 22 the Tree property on ObjectExplorerService is non-public; use
            // non-public reflection to retrieve the ObjectExplorerControl (which extends
            // System.Windows.Forms.TreeView) so that node text-traversal and Expand() work.
            var treeView = GetOeTreeView(explorerService);
            if (treeView is null) return;

            System.Windows.Forms.TreeNode? serverNode = null;
            foreach (System.Windows.Forms.TreeNode root in treeView.Nodes)
            {
                if (TreeNodeMatchesServer(root.Text, reference.InstanceName))
                {
                    serverNode = root;
                    break;
                }
            }
            if (serverNode is null) return;
            if (!serverNode.IsExpanded) serverNode.Expand();

            var isCatNode = FindChildTreeNodeByText(serverNode.Nodes, "Integration Services Catalogs");
            if (isCatNode is null) return;
            if (!isCatNode.IsExpanded) isCatNode.Expand();
            expandedUrns.Add("SSIS_TV_IS_CATALOGS");

            var ssisDbNode = FindChildTreeNodeByText(isCatNode.Nodes, "SSISDB");
            if (ssisDbNode is null) return;
            if (!ssisDbNode.IsExpanded) ssisDbNode.Expand();
            expandedUrns.Add("SSIS_TV_SSISDB");

            if (string.IsNullOrEmpty(reference.SchemaName)) return;
            var folderNode = FindChildTreeNodeByText(ssisDbNode.Nodes, reference.SchemaName);
            if (folderNode is null) return;
            if (!folderNode.IsExpanded) folderNode.Expand();
            expandedUrns.Add($"SSIS_TV_FOLDER:{reference.SchemaName}");

            var projectsFolderNode = FindChildTreeNodeByText(folderNode.Nodes, "Projects");
            if (projectsFolderNode is null) return;
            if (!projectsFolderNode.IsExpanded) projectsFolderNode.Expand();
            expandedUrns.Add("SSIS_TV_PROJECTS_FOLDER");

            if (string.IsNullOrEmpty(reference.ParentObjectName)) return;
            var projectNode = FindChildTreeNodeByText(projectsFolderNode.Nodes, reference.ParentObjectName);
            if (projectNode is null) return;
            if (!projectNode.IsExpanded) projectNode.Expand();
            expandedUrns.Add($"SSIS_TV_PROJECT:{reference.ParentObjectName}");

            var packagesFolderNode = FindChildTreeNodeByText(projectNode.Nodes, "Packages");
            if (packagesFolderNode is null) return;
            if (!packagesFolderNode.IsExpanded) packagesFolderNode.Expand();
            expandedUrns.Add("SSIS_TV_PACKAGES_FOLDER");
        }
        catch { }
    }

    /// <summary>
    /// Locates the SSIS package <see cref="System.Windows.Forms.TreeNode"/> by traversing the
    /// OE TreeView by text and calls <c>SynchronizeTree</c> on it to scroll OE to the package.
    /// Expansion and search are performed in a single pass: each level is expanded as needed
    /// and only returns <see langword="false"/> when a child is not yet present (async loading
    /// still in progress). The next retry will find it populated.
    /// </summary>
    private static bool TryFindSsisPackageViaTreeView(
        SqlObjectReference reference, object explorerService, MethodInfo syncMethod, HashSet<string> expandedUrns)
    {
        try
        {
            var treeView = GetOeTreeView(explorerService);
            if (treeView is null) return false;

            System.Windows.Forms.TreeNode? serverNode = null;
            foreach (System.Windows.Forms.TreeNode root in treeView.Nodes)
            {
                if (TreeNodeMatchesServer(root.Text, reference.InstanceName))
                {
                    serverNode = root;
                    break;
                }
            }
            if (serverNode is null) return false;
            if (!serverNode.IsExpanded) serverNode.Expand();

            var isCatNode = FindChildTreeNodeByText(serverNode.Nodes, "Integration Services Catalogs");
            if (isCatNode is null) return false;
            if (!isCatNode.IsExpanded) isCatNode.Expand();

            var ssisDbNode = FindChildTreeNodeByText(isCatNode.Nodes, "SSISDB");
            if (ssisDbNode is null) return false;
            if (!ssisDbNode.IsExpanded) ssisDbNode.Expand();

            var folderNode = FindChildTreeNodeByText(ssisDbNode.Nodes, reference.SchemaName);
            if (folderNode is null) return false;
            if (!folderNode.IsExpanded) folderNode.Expand();

            var projectsFolderNode = FindChildTreeNodeByText(folderNode.Nodes, "Projects");
            if (projectsFolderNode is null) return false;
            if (!projectsFolderNode.IsExpanded) projectsFolderNode.Expand();

            var projectNode = FindChildTreeNodeByText(projectsFolderNode.Nodes, reference.ParentObjectName ?? string.Empty);
            if (projectNode is null) return false;
            if (!projectNode.IsExpanded) projectNode.Expand();

            var packagesFolderNode = FindChildTreeNodeByText(projectNode.Nodes, "Packages");
            if (packagesFolderNode is null) return false;
            if (!packagesFolderNode.IsExpanded) packagesFolderNode.Expand();

            var packageNode = FindChildTreeNodeByText(packagesFolderNode.Nodes, reference.ObjectName);
            if (packageNode is null) return false;

            // SynchronizeTree(INodeInformation) — try the TreeNode's Tag first because in
            // SSMS 22 each OE TreeNode carries its INodeInformation/NodeContext as the Tag.
            // If that fails, fall back to making the node selected and visible directly.
            bool synced = false;
            if (packageNode.Tag is not null)
            {
                try { syncMethod.Invoke(explorerService, new object[] { packageNode.Tag }); synced = true; } catch { }
            }
            if (!synced)
            {
                try { syncMethod.Invoke(explorerService, new object[] { packageNode }); synced = true; } catch { }
            }
            if (!synced)
            {
                // Last resort: select and scroll to the node through the raw TreeView.
                treeView.SelectedNode = packageNode;
                packageNode.EnsureVisible();
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Expands the SQL Server Agent → Jobs subtree using direct TreeView text-traversal,
    /// avoiding any <c>FindNode</c> call that would require a <c>@CategoryID</c> predicate
    /// and block the UI thread.
    /// </summary>
    private static void TryExpandSqlAgentJobsViaTreeView(
        SqlObjectReference reference, object explorerService, HashSet<string> expandedUrns)
    {
        try
        {
            var treeView = GetOeTreeView(explorerService);
            if (treeView is null) return;

            System.Windows.Forms.TreeNode? serverNode = null;
            foreach (System.Windows.Forms.TreeNode root in treeView.Nodes)
            {
                if (TreeNodeMatchesServer(root.Text, reference.InstanceName))
                {
                    serverNode = root;
                    break;
                }
            }
            if (serverNode is null) return;
            if (!serverNode.IsExpanded) serverNode.Expand();

            // Node text may include a status suffix: "SQL Server Agent (Agent XPs disabled)".
            // FindChildTreeNodeByText handles this via its StartsWith(" ") check.
            var agentNode = FindChildTreeNodeByText(serverNode.Nodes, "SQL Server Agent");
            if (agentNode is null) return;
            if (!agentNode.IsExpanded) agentNode.Expand();
            expandedUrns.Add("AGENT_TV_AGENT");

            var jobsNode = FindChildTreeNodeByText(agentNode.Nodes, "Jobs");
            if (jobsNode is null) return;
            if (!jobsNode.IsExpanded) jobsNode.Expand();
            expandedUrns.Add("AGENT_TV_JOBS");
        }
        catch { }
    }

    /// <summary>
    /// Locates a SQL Server Agent job by traversing the OE TreeView by text and calls
    /// <c>SynchronizeTree</c> on it. Avoids <c>FindNode</c> with a bare
    /// <c>Job[@Name='...']</c> URN, which hangs because SSMS requires the <c>@CategoryID</c>
    /// predicate to resolve the node without a full enumeration on the UI thread.
    /// </summary>
    private static bool TryFindSqlAgentJobViaTreeView(
        SqlObjectReference reference, object explorerService, MethodInfo syncMethod)
    {
        try
        {
            var treeView = GetOeTreeView(explorerService);
            if (treeView is null) return false;

            System.Windows.Forms.TreeNode? serverNode = null;
            foreach (System.Windows.Forms.TreeNode root in treeView.Nodes)
            {
                if (TreeNodeMatchesServer(root.Text, reference.InstanceName))
                {
                    serverNode = root;
                    break;
                }
            }
            if (serverNode is null) return false;
            if (!serverNode.IsExpanded) serverNode.Expand();

            var agentNode = FindChildTreeNodeByText(serverNode.Nodes, "SQL Server Agent");
            if (agentNode is null) return false;
            if (!agentNode.IsExpanded) agentNode.Expand();

            var jobsNode = FindChildTreeNodeByText(agentNode.Nodes, "Jobs");
            if (jobsNode is null) return false;
            if (!jobsNode.IsExpanded) jobsNode.Expand();

            var jobNode = FindChildTreeNodeByText(jobsNode.Nodes, reference.ObjectName);
            if (jobNode is null) return false;

            bool synced = false;
            if (jobNode.Tag is not null)
            {
                try { syncMethod.Invoke(explorerService, new object[] { jobNode.Tag }); synced = true; } catch { }
            }
            if (!synced)
            {
                try { syncMethod.Invoke(explorerService, new object[] { jobNode }); synced = true; } catch { }
            }
            if (!synced)
            {
                treeView.SelectedNode = jobNode;
                jobNode.EnsureVisible();
            }
            return true;
        }
        catch { return false; }
    }

    private static System.Windows.Forms.TreeNode? FindChildTreeNodeByText(
        System.Windows.Forms.TreeNodeCollection? nodes, string? text)
    {
        if (nodes is null || string.IsNullOrEmpty(text)) return null;
        foreach (System.Windows.Forms.TreeNode node in nodes)
        {
            if (string.Equals(node.Text, text, StringComparison.OrdinalIgnoreCase))
                return node;
            // Nodes may have extra info appended (e.g. server nodes include version + username).
            if (node.Text.StartsWith(text + " ", StringComparison.OrdinalIgnoreCase) ||
                node.Text.StartsWith(text + "(", StringComparison.OrdinalIgnoreCase))
                return node;
        }
        return null;
    }

    private static bool TreeNodeMatchesServer(string nodeText, string instanceName)
    {
        foreach (string variation in GetServerNameVariations(instanceName))
        {
            if (nodeText.StartsWith(variation + " ", StringComparison.OrdinalIgnoreCase) ||
                nodeText.StartsWith(variation + "(", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nodeText, variation, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds a node by URN and calls <see cref="System.Windows.Forms.TreeNode.Expand"/> on it
    /// if it is not already expanded, then records the URN so future calls are instant no-ops.
    /// Returns <see langword="true"/> if the node was found in the tree (regardless of whether
    /// Expand was needed), <see langword="false"/> if the node is not yet visible in the tree.
    /// </summary>
    private static bool ExpandNode(MethodInfo findNodeMethod, object explorerService, string urn, HashSet<string> expandedUrns)
    {
        if (expandedUrns.Contains(urn)) return true;
        try
        {
            object? result = findNodeMethod.Invoke(explorerService, new object[] { urn });
            if (result is null) return false;

            // SSMS 18 and earlier: FindNode returns the WinForms TreeNode directly.
            // SSMS 22: FindNode returns a NodeContext whose non-public TreeNode property
            // holds the actual WinForms TreeNode. Try both paths.
            System.Windows.Forms.TreeNode? treeNode =
                result as System.Windows.Forms.TreeNode ??
                result.GetType()
                      .GetProperty("TreeNode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                      ?.GetValue(result) as System.Windows.Forms.TreeNode;

            if (treeNode is not null)
            {
                if (!treeNode.IsExpanded)
                    treeNode.Expand();
                expandedUrns.Add(urn);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Returns the <see cref="System.Windows.Forms.TreeView"/> used by SSMS Object Explorer.
    /// In SSMS 22 the <c>Tree</c> property on <c>ObjectExplorerService</c> is non-public and
    /// returns an <c>ObjectExplorerControl</c> that extends <see cref="System.Windows.Forms.TreeView"/>.
    /// Older SSMS versions expose it as a public property.
    /// </summary>
    private static System.Windows.Forms.TreeView? GetOeTreeView(object explorerService)
    {
        try
        {
            var prop = explorerService.GetType().GetProperty(
                "Tree", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(explorerService) as System.Windows.Forms.TreeView;
        }
        catch { return null; }
    }

    private static bool TryFindAndSynchronize(SqlObjectReference reference, string resolvedServerUrn, MethodInfo findNodeMethod, MethodInfo syncMethod, object explorerService)
    {
        foreach (string urn in BuildCandidateUrns(reference, resolvedServerUrn))
        {
            object? node = findNodeMethod.Invoke(explorerService, new object[] { urn });
            if (node is null)
            {
                continue;
            }

            syncMethod.Invoke(explorerService, new[] { node });
            return true;
        }

        return false;
    }

    #endregion Node Expansion and Location

    #region Type Resolution

    private static Type? ResolvePackageType()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType("Microsoft.VisualStudio.Shell.Package", false))
            .FirstOrDefault(t => t is not null);
    }

    #endregion Type Resolution

    #region URN Building

    private static IEnumerable<string> BuildCandidateUrns(SqlObjectReference reference, string serverUrn)
    {
        // SSMS 22 may return simplified URNs like "Server", "Server1", etc.
        // Only wrap in Server[@Name='...'] if this is a raw server name, not already a valid URN segment.
        if (!serverUrn.StartsWith("Server", StringComparison.OrdinalIgnoreCase))
        {
            serverUrn = $"Server[@Name='{Escape(serverUrn)}']";
        }

        // SQL Server Agent jobs — live under JobServer, not under a database.
        if (string.Equals(reference.ObjectType, "SQL_AGENT_JOB", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{serverUrn}/JobServer/Job[@Name='{Escape(reference.ObjectName)}']";
            yield break;
        }

        // SSIS packages — live under the Integration Services Catalogs hierarchy.
        // Folder name is stored in SchemaName; project name in ParentObjectName.
        if (string.Equals(reference.ObjectType, "SSIS_PACKAGE", StringComparison.OrdinalIgnoreCase))
        {
            string folder  = Escape(reference.SchemaName);
            string project = Escape(reference.ParentObjectName ?? string.Empty);
            string package = Escape(reference.ObjectName);
            yield return $"{serverUrn}/IntegrationServicesCatalog[@Name='SSISDB']/CatalogFolder[@Name='{folder}']/ProjectInfo[@Name='{project}']/PackageInfo[@Name='{package}']";
            yield break;
        }

        string database = Escape(reference.DatabaseName);
        string schema   = Escape(reference.SchemaName);
        string name     = Escape(reference.ObjectName);
        string dbUrn    = $"{serverUrn}/Database[@Name='{database}']";

        // Child objects (constraints, DML triggers) – navigate through their parent table or view.
        if (IsChildObject(reference.ObjectType) && !string.IsNullOrEmpty(reference.ParentObjectName))
        {
            string parentSchema  = Escape(reference.ParentSchemaName ?? reference.SchemaName);
            string parentName    = Escape(reference.ParentObjectName);
            string primaryParent = reference.ParentObjectType?.Equals("VIEW", StringComparison.OrdinalIgnoreCase) == true
                ? "View" : "Table";
            string alterParent   = primaryParent == "Table" ? "View" : "Table";

            // DEFAULT_CONSTRAINT is not a direct collection on the Table SMO object — it is
            // a property of each Column (Column.DefaultConstraint). SSMS Object Explorer has
            // no DefaultConstraint folder under the table, so FindNode with a
            // Table/.../DefaultConstraint path triggers a lazy column-level load that blocks
            // the UI thread. Navigate to the parent table instead, which is the closest
            // reachable ancestor and is already expanded by TryExpandParentNodes.
            if (string.Equals(reference.ObjectType, "DEFAULT_CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{dbUrn}/{primaryParent}[@Name='{parentName}' and @Schema='{parentSchema}']";
                yield return $"{dbUrn}/{alterParent}[@Name='{parentName}' and @Schema='{parentSchema}']";
                yield break;
            }

            string childNode = GetChildTypeNode(reference.ObjectType);

            yield return $"{dbUrn}/{primaryParent}[@Name='{parentName}' and @Schema='{parentSchema}']/{childNode}[@Name='{name}']";
            yield return $"{dbUrn}/{alterParent}[@Name='{parentName}' and @Schema='{parentSchema}']/{childNode}[@Name='{name}']";
            yield break;
        }

        // Service Broker queues live under the ServiceBroker intermediate node.
        if (string.Equals(reference.ObjectType, "SERVICE_QUEUE", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{dbUrn}/ServiceBroker/ServiceQueue[@Name='{name}' and @Schema='{schema}']";
            yield return $"{dbUrn}/ServiceBroker/ServiceQueue[@Name='{schema}.{name}']";
            yield break;
        }

        // Database DDL triggers have no schema.
        if (string.Equals(reference.ObjectType, "DATABASE_DDL_TRIGGER", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{dbUrn}/DatabaseDdlTrigger[@Name='{name}']";
            yield break;
        }

        // Standard schema-scoped database-level objects.
        var typeNodes = GetTypeNodes(reference.ObjectType);
        foreach (string typeNode in typeNodes)
        {
            yield return $"{dbUrn}/{typeNode}[@Name='{name}' and @Schema='{schema}']";
            yield return $"{dbUrn}/{typeNode}[@Name='{schema}.{name}']";
        }
    }

    private static bool IsChildObject(string? objectType) =>
        objectType?.ToUpperInvariant() switch
        {
            "CHECK_CONSTRAINT"       => true,
            "DEFAULT_CONSTRAINT"     => true,
            "FOREIGN_KEY_CONSTRAINT" => true,
            "PRIMARY_KEY_CONSTRAINT" => true,
            "UNIQUE_CONSTRAINT"      => true,
            "SQL_TRIGGER"            => true,
            "CLR_TRIGGER"            => true,
            _                        => false
        };

    private static string GetChildTypeNode(string? objectType) =>
        objectType?.ToUpperInvariant() switch
        {
            "CHECK_CONSTRAINT"       => "Check",
            "DEFAULT_CONSTRAINT"     => "DefaultConstraint",
            "FOREIGN_KEY_CONSTRAINT" => "ForeignKey",
            "PRIMARY_KEY_CONSTRAINT" => "Index",
            "UNIQUE_CONSTRAINT"      => "Index",
            "SQL_TRIGGER"            => "Trigger",
            "CLR_TRIGGER"            => "Trigger",
            _                        => "Check"
        };

    private static IEnumerable<string> GetTypeNodes(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
        {
            return new[] { "Table", "View", "StoredProcedure", "UserDefinedFunction", "Synonym" };
        }

        return objectType?.ToUpperInvariant() switch
        {
            "USER_TABLE"                       => new[] { "Table" },
            "VIEW"                             => new[] { "View" },
            "SQL_STORED_PROCEDURE"             => new[] { "StoredProcedure" },
            "CLR_STORED_PROCEDURE"             => new[] { "StoredProcedure" },
            "EXTENDED_STORED_PROCEDURE"        => new[] { "ExtendedStoredProcedure" },
            "SQL_TABLE_VALUED_FUNCTION"        => new[] { "UserDefinedFunction" },
            "SQL_INLINE_TABLE_VALUED_FUNCTION" => new[] { "UserDefinedFunction" },
            "SQL_SCALAR_FUNCTION"              => new[] { "UserDefinedFunction" },
            "CLR_SCALAR_FUNCTION"              => new[] { "UserDefinedFunction" },
            "CLR_TABLE_VALUED_FUNCTION"        => new[] { "UserDefinedFunction" },
            "CLR_AGGREGATE_FUNCTION"           => new[] { "UserDefinedAggregate" },
            "SYNONYM"                          => new[] { "Synonym" },
            "SEQUENCE_OBJECT"                  => new[] { "Sequence" },
            "RULE"                             => new[] { "Rule" },
            "TYPE_TABLE"                       => new[] { "UserDefinedTableType" },
            "USER_DEFINED_TYPE"                => new[] { "UserDefinedDataType" },
            "XML_SCHEMA_COLLECTION"            => new[] { "XmlSchemaCollection" },
            _                                  => new[] { "Table", "View", "StoredProcedure", "UserDefinedFunction", "Synonym" }
        };
    }

    private static string Escape(string? value) => (value ?? string.Empty).Replace("'", "''");

    #endregion URN Building

    #region Diagnostics

    /// <summary>
    /// Collects every server name reachable from a root node's properties.
    /// This checks each connection property path individually so that
    /// UIConnectionInfo.ServerName (which preserves a SQL alias) is always
    /// included even when a Connection or ConnectionContext property exists
    /// and resolves ServerName to the actual server name.
    /// </summary>
    private static List<string> CollectNodeServerNames(object rootNode)
    {
        var names = new List<string>();

        string? displayName = GetPropertyValue(rootNode, "Name")?.ToString();
        if (!string.IsNullOrWhiteSpace(displayName))
            names.Add(displayName!);

        // Check each connection property path individually — do NOT short-circuit.
        // "Connection" (ServerConnection) typically resolves ServerName to the actual name.
        // "UIConnectionInfo" preserves the original input (e.g. a SQL alias).
        foreach (string? connPropName in new[] { "Connection", "ConnectionContext", "UIConnectionInfo" })
        {
            object? connObj = GetPropertyValue(rootNode, connPropName);
            if (connObj is null) continue;

            foreach (string? serverPropName in new[] { "ServerName", "ServerInstance", "DataSource" })
            {
                string? val = GetPropertyValue(connObj, serverPropName)?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    names.Add(val!);
            }

            // UIConnectionInfo may be nested under Connection or ConnectionContext.
            object? nestedUiConn = GetPropertyValue(connObj, "UIConnectionInfo");
            if (nestedUiConn is not null)
            {
                string? val = GetPropertyValue(nestedUiConn, "ServerName")?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    names.Add(val!);
            }
        }

        return names;
    }

    /// <summary>
    /// Temporary diagnostic helper – collects detailed info about all root nodes
    /// so we can see exactly what properties are available and what values they hold.
    /// </summary>
    private static string CollectDiagnosticInfo(IObjectExplorerService oeService, string instanceName)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Input instanceName: [{instanceName}]");

            var candidates = GetServerNameVariations(instanceName).ToList();
            sb.AppendLine($"Candidates ({candidates.Count}): [{string.Join("; ", candidates)}]");

            var type = oeService.GetType();
            sb.AppendLine($"Service runtime type: {type.FullName} from [{type.Assembly.GetName().Name}]");

            // List all public methods on the service so we can see what's available
            sb.AppendLine("Public methods on service type:");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName)
                .OrderBy(m => m.Name))
            {
                string parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                sb.AppendLine($"  {m.Name}({parms}) -> {m.ReturnType.Name}");
            }

            // List interfaces
            sb.AppendLine("Interfaces:");
            foreach (var iface in type.GetInterfaces().Where(i => i.Namespace?.Contains("SqlServer") == true || i.Namespace?.Contains("ObjectExplorer") == true))
            {
                sb.AppendLine($"  {iface.FullName}");
                foreach (var m in iface.GetMethods().Where(m => !m.IsSpecialName))
                {
                    string parms = string.Join(", ", m.GetParameters().Select(p => $"{(p.IsOut ? "out " : "")}{p.ParameterType.Name} {p.Name}"));
                    sb.AppendLine($"    {m.Name}({parms}) -> {m.ReturnType.Name}");
                }
            }

            // Test FindNode with each candidate server name variation
            sb.AppendLine("--- FindNode tests ---");
            var findNode = type.GetMethod("FindNode", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (findNode is null)
            {
                sb.AppendLine("  FindNode method not found!");
            }
            else
            {
                foreach (string? serverName in candidates)
                {
                    string testUrn = $"Server[@Name='{Escape(serverName)}']";
                    try
                    {
                        object? node = findNode.Invoke(oeService, new object[] { testUrn });
                        if (node is not null)
                        {
                            string? nodeUrn = GetPropertyValue(node, "UrnPath", "Urn")?.ToString();
                            string? nodeName = GetPropertyValue(node, "Name")?.ToString();
                            string? nodeDisplay = null;
                            string? displayPropUsed = null;
                            foreach (string dp in new[] { "InvariantName", "DisplayName", "Label", "DisplayText", "Caption" })
                            {
                                nodeDisplay = GetPropertyValue(node, dp)?.ToString();
                                if (!string.IsNullOrEmpty(nodeDisplay)) { displayPropUsed = dp; break; }
                            }
                            sb.AppendLine($"  FindNode(\"{testUrn}\"): FOUND! Name=[{nodeName}] UrnPath=[{nodeUrn}] {displayPropUsed}=[{nodeDisplay}]");
                            var collected = CollectNodeServerNames(node);
                            sb.AppendLine($"    CollectedNames: [{string.Join("; ", collected)}]");
                        }
                        else
                        {
                            sb.AppendLine($"  FindNode(\"{testUrn}\"): null");
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"  FindNode(\"{testUrn}\"): EXCEPTION: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }

                // Also try GetSelectedNodes to see what a real node URN looks like
                sb.AppendLine("--- GetSelectedNodes ---");
                try
                {
                    var getSelected = type.GetMethod("GetSelectedNodes", BindingFlags.Public | BindingFlags.Instance);
                    if (getSelected is not null)
                    {
                        object[] selArgs = new object[] { 0, null! };
                        getSelected.Invoke(oeService, selArgs);
                        if (selArgs[1] is Array selNodes && selNodes.Length > 0)
                        {
                            for (int si = 0; si < selNodes.Length; si++)
                            {
                                object? sn = selNodes.GetValue(si);
                                if (sn is null) continue;
                                string? snDisplay = null;
                                string? snDisplayProp = null;
                                foreach (string dp in new[] { "InvariantName", "DisplayName", "Label", "DisplayText", "Caption" })
                                {
                                    snDisplay = GetPropertyValue(sn, dp)?.ToString();
                                    if (!string.IsNullOrEmpty(snDisplay)) { snDisplayProp = dp; break; }
                                }
                                sb.AppendLine($"  Selected[{si}] Name=[{GetPropertyValue(sn, "Name")}] UrnPath=[{GetPropertyValue(sn, "UrnPath", "Urn")}] {snDisplayProp}=[{snDisplay}]");
                            }
                        }
                        else
                        {
                            sb.AppendLine("  No nodes selected.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  GetSelectedNodes error: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Diagnostic error: {ex}";
        }
    }

    #endregion Diagnostics

    #region Server URN Resolution

    /// <summary>
    /// Finds the Object Explorer server root node that hosts the Integration Services
    /// Catalog (SSISDB). In SSMS 22 with the IS integration feature installed, IS Catalog
    /// lives under a separate <em>Integration Services</em> connection root — not under the
    /// regular SQL Server connection root returned by <see cref="GetServerUrn"/>.
    /// </summary>
    /// <returns>
    /// The full URN context of the root that has IS Catalog as a direct child (e.g.
    /// <c>Server[@Name='myserver']</c>), or <see cref="string.Empty"/> if not found.
    /// </returns>
    private static string FindIsCatalogServerUrn(MethodInfo findNodeMethod, object explorerService, string instanceName)
    {
        // 1. Try standard server-name variations with the full IS Catalog path appended.
        //    Works when IS Catalog is under the same OE root as the SQL Server connection
        //    (SSMS 20 behaviour, or SSMS 22 when IS Catalog is co-hosted on that root).
        foreach (string serverName in GetServerNameVariations(instanceName))
        {
            string serverUrn = $"Server[@Name='{Escape(serverName)}']";
            try
            {
                if (findNodeMethod.Invoke(explorerService, new object[] { $"{serverUrn}/IntegrationServicesCatalog[@Name='SSISDB']" }) is not null)
                    return serverUrn;
            }
            catch { }

            // Also check the collection folder (no @Name) — visible once the server root
            // is expanded, even before the SSISDB catalog node has been loaded.
            try
            {
                if (findNodeMethod.Invoke(explorerService, new object[] { $"{serverUrn}/IntegrationServicesCatalog" }) is not null)
                    return serverUrn;
            }
            catch { }
        }

        // 2. In SSMS 22 the IS connection is a separate OE root whose Context can differ
        //    from the SQL Server root (different alias, port, or connection-type marker).
        //    Enumerate every root node in the OE TreeView and test each for IS Catalog,
        //    regardless of the server name shown.
        try
        {
            var treeView = GetOeTreeView(explorerService);
            if (treeView is not null)
            {
                foreach (System.Windows.Forms.TreeNode rootNode in treeView.Nodes)
                {
                    object? tag = rootNode.Tag;
                    if (tag is null) continue;

                    string? context = GetPropertyValue(tag, "Context")?.ToString();
                    if (string.IsNullOrWhiteSpace(context)) continue;

                    try
                    {
                        if (findNodeMethod.Invoke(explorerService, new object[] { $"{context}/IntegrationServicesCatalog[@Name='SSISDB']" }) is not null ||
                            findNodeMethod.Invoke(explorerService, new object[] { $"{context}/IntegrationServicesCatalog" }) is not null)
                            return context!;
                    }
                    catch { }
                }
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the OE server <paramref name="node"/>'s connection
    /// port is compatible with the port encoded in <paramref name="instanceName"/>.
    /// When <paramref name="instanceName"/> carries no port the check always passes.
    /// When the node's connection reports no port, standard SQL Server port 1433 is assumed.
    /// </summary>
    private static bool ServerPortMatches(object node, string instanceName)
    {
        string requestedPort = ExtractPort(instanceName);
        if (string.IsNullOrEmpty(requestedPort)) return true; // no port constraint

        foreach (string connPropName in new[] { "Connection", "ConnectionContext" })
        {
            object? conn = GetPropertyValue(node, connPropName);
            if (conn is null) continue;

            foreach (string serverPropName in new[] { "ServerName", "ServerInstance", "DataSource" })
            {
                string? connServer = GetPropertyValue(conn, serverPropName)?.ToString();
                if (string.IsNullOrEmpty(connServer)) continue;

                string connPort = ExtractPort(connServer);
                // If the connection reports a port, it must match the requested port.
                // If the connection reports no port, assume the default 1433.
                return string.IsNullOrEmpty(connPort)
                    ? string.Equals(requestedPort, "1433", StringComparison.Ordinal)
                    : string.Equals(requestedPort, connPort, StringComparison.OrdinalIgnoreCase);
            }
        }

        return true; // Cannot determine port — allow the match.
    }

    private static string GetServerUrn(IObjectExplorerService oeService, string instanceName, string databaseName = "")
    {
        try
        {
            var findNodeMethod = oeService.GetType().GetMethod("FindNode",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (findNodeMethod is null) return string.Empty;

            // 1. Try FindNode with name variations of the instance name.
            //    Covers direct server names and older SSMS versions that use the alias in the URN.
            foreach (string serverName in GetServerNameVariations(instanceName))
            {
                string candidateUrn = $"Server[@Name='{Escape(serverName)}']";
                try
                {
                    object? node = findNodeMethod.Invoke(oeService, new object[] { candidateUrn });
                    if (node is not null)
                    {
                        // Port-aware guard: a port-qualified instance name (e.g. SERVER1,12345)
                        // must not be satisfied by a node whose connection uses a different port
                        // (e.g. SERVER1,1433). GetServerNameVariations produces a no-port variation
                        // "SERVER1" that would otherwise silently match the wrong connection and
                        // prevent the code from ever prompting the user to connect on the right port.
                        if (!ServerPortMatches(node, instanceName))
                            continue;

                        // Use Context (full XPath, e.g. "Server[@Name='S1WDVSQLWEB1\WEBDEV']") not
                        // UrnPath (type template "Server"). UrnPath strips the @Name predicate so
                        // subsequent FindNode("Server") returns a disconnected placeholder whose
                        // DatabasesFolder has no connection info, crashing OnBuildingChildren.
                        string? context = GetPropertyValue(node, "Context")?.ToString();
                        return !string.IsNullOrWhiteSpace(context) ? context! : candidateUrn;
                    }
                }
                catch { }
            }

            // 2. SSMS 22 removed the @Name qualifier from simplified URNs — FindNode("Server") returns
            //    null. The only reliable way to discover connected servers is via GetSelectedNodes:
            //    any selected node (at any depth) exposes Connection.ServerName (the alias) and its
            //    Parent chain leads to the server root whose Context holds the full XPath expression
            //    (e.g. "Server[@Name='S1WDVSQLWEB1\WEBDEV']") that FindNode actually accepts.
            var getSelectedMethod = oeService.GetType().GetMethod("GetSelectedNodes",
                BindingFlags.Public | BindingFlags.Instance);
            if (getSelectedMethod is not null)
            {
                object[] selArgs = new object[] { 0, null! };
                try { getSelectedMethod.Invoke(oeService, selArgs); } catch { }

                if (selArgs[1] is Array selNodes)
                {
                    for (int si = 0; si < selNodes.Length; si++)
                    {
                        object? sn = selNodes.GetValue(si);
                        if (sn is null) continue;

                        // Every node in the tree carries the connection alias on Connection.ServerName.
                        string? snConnServerName = GetPropertyValue(
                            GetPropertyValue(sn, "Connection"), "ServerName")?.ToString();
                        if (string.IsNullOrEmpty(snConnServerName)) continue;
                        if (!string.Equals(snConnServerName, instanceName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Walk up the Parent chain to reach the server root node (the one with no parent).
                        object? current = sn;
                        while (current is not null)
                        {
                            object? parent = GetPropertyValue(current, "Parent");
                            if (parent is null)
                            {
                                // current IS the server root — return its full XPath context.
                                string? serverContext = GetPropertyValue(current, "Context")?.ToString();
                                if (!string.IsNullOrEmpty(serverContext))
                                    return serverContext!;
                                break;
                            }
                            current = parent;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    #endregion Server URN Resolution
}