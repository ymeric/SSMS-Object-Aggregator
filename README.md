# SSMS Object Aggregator

`SSMS Object Aggregator` is an SSMS 22 add-on that adds a dockable **Object Aggregator** tool window to help you collect and browse SQL objects from multiple SQL Server instances and databases in one place.

Instead of expanding many connections in Object Explorer, you define a **group** of instance/database targets, optionally add filters, and then load the combined result set into a single tree.

## Purpose

This add-on is designed to make cross-instance object discovery faster inside SSMS.

Typical use cases:

- compare naming patterns across environments
- review all related objects for a feature area
- collect objects from multiple databases into one list
- jump from an aggregated result back to **Object Explorer**
- browse SQL Agent jobs and SSIS packages alongside regular database objects

## How to open it

In SSMS 22, open:

- __Tools > Object Aggregator__

This shows the dockable **Object Aggregator** window.

## Core concepts

### Group
A saved container for one logical collection of sources.

Example:

- `Customer Sync`
- `Billing Objects`
- `Release Validation`

### Instance/Database entry
A source inside a group.

Example:

- `SQLDEV01 / SalesDb`
- `SQLQA01 / SalesDb`
- `SQLPROD01 / BillingDb`

### Filter definition
An optional schema/object name filter applied to one instance/database entry.

Example filters:

- Schema: `dbo`, Object: `usp*`
- Schema: `etl`, Object: `LoadCustomer*`
- Schema: blank, Object: `cust`

Plain text behaves like a contains search. Wildcards are supported:

- `*` = any number of characters
- `?` = single character

## Features

### 1. Dockable aggregated object browser
The add-on adds a tool window named **Object Aggregator** inside SSMS.

What it does:

- stays inside the SSMS shell
- shows saved groups in a tree
- loads objects on demand when a group is expanded

UI example:

1. Open __Tools > Object Aggregator__
2. Dock the window beside **Object Explorer**
3. Expand a group to load its objects

---

### 2. Saved groups
You can create named groups and keep them for later sessions.

What it does:

- groups are persisted automatically
- groups are sorted by name
- renaming and deletion are built in

UI interactions:

- toolbar: **Add Group**, **Edit Group**, **Delete Group**, **Reload Group**
- menu: __Groups > Add Group__
- right-click a group: **Rename**, **Add Instance**, **Edit**, **Delete**
- keyboard:
  - `F2` = rename selected group
  - `Delete` = delete selected group
  - `Enter` = edit selected group

Example:

Create a group named `Customer Objects`, then use it to collect related objects from `Dev`, `QA`, and `Prod`.

---

### 3. Multiple instance/database targets per group
Each group can contain multiple SQL Server instance/database pairs.

What it does:

- one group can aggregate results from several environments
- each source can have its own filters
- duplicate objects are de-duplicated in the final result set

UI example:

1. Create group `Customer Objects`
2. Right-click the group and choose **Add Instance**
3. Add:
   - `SQLDEV01` / `SalesDb`
   - `SQLQA01` / `SalesDb`
   - `SQLPROD01` / `SalesDb`

After reload, all matching objects appear under one group.

---

### 4. Per-source filter definitions
Each instance/database entry can contain one or more filter definitions.

What it does:

- filter by schema name
- filter by object name
- combine multiple filters for the same source
- multiple filter rows act like an **OR**
- schema/object values inside one row act together

UI example:

1. Edit a group
2. Select `SQLDEV01 / SalesDb`
3. Click **Filter Definitions**
4. Add filters such as:

| Schema filter | Object filter |
|---|---|
| `dbo` | `usp*` |
| `etl` | `Load*` |
|  | `Customer` |

Meaning:

- `dbo` + `usp*` finds objects in schema `dbo` whose name matches `usp*`
- `etl` + `Load*` finds ETL loader objects
- `Customer` alone finds any object whose name contains `Customer`

---

### 5. Hierarchical result tree
Loaded objects are organized into a readable hierarchy.

Tree structure:

- Group
  - Instance / Database
    - Object Type
      - Object

Example:

- `Customer Objects`
  - `SQLDEV01 / SalesDb`
    - `Tables`
      - `dbo.Customer`
    - `Stored Procedures`
      - `dbo.uspGetCustomer`
    - `Views`
      - `reporting.vwCustomerSummary`

This makes mixed results easier to scan than a flat list.

---

### 6. Quick search inside each source branch
Each `Instance / Database` node includes a quick filter box.

What it does:

- filters loaded object names within that branch
- grouped object types remain visible only when they still contain matches
- `Esc` clears the search box

Behavior:

- filtering activates after at least **3 characters**
- input is debounced, so it applies shortly after typing stops

UI example:

1. Expand `SQLDEV01 / SalesDb`
2. In the small filter box under that node, type `cust`
3. The tree narrows to objects whose display name contains `cust`
4. Press `Esc` to clear the filter

---

### 7. One-click reload for current definitions
Groups load lazily and can be refreshed at any time.

What it does:

- expanding a group triggers the first load
- **Reload Group** refreshes results after definition changes
- status text shows messages such as:
  - `Loaded 24 object(s).`
  - `No matches.`

UI example:

1. Add a new filter definition
2. Return to the main window
3. Click **Reload Group**
4. Review the updated aggregated result set

---

### 8. Locate objects in Object Explorer
You can navigate from an aggregated object back into SSMS Object Explorer.

What it does:

- double-clicking an object attempts to locate it
- object context menu includes **Locate**
- useful when the aggregated tree is used as a finder, not just a report

UI example:

1. Expand a group
2. Open `Stored Procedures`
3. Double-click `dbo.uspGetCustomer`

Expected result:

- SSMS opens or focuses **Object Explorer**
- the add-on attempts to scroll to the matching object node

---

### 9. SSIS and SQL Agent support
A source can target SSIS-related objects by using `SSIS` as the database name.

What it does:

- loads **SQL Agent Jobs** from `msdb`
- loads **SSIS Packages** from `SSISDB`
- shows them in the same aggregated tree as database objects

Example source:

- `SQLETL01 / SSIS`

Example result:

- `SQLETL01 / SSIS`
  - `SQL Agent Jobs`
    - `Nightly Customer Import`
  - `SSIS Packages`
    - `LoadCustomers.dtsx`

This is useful for operational or ETL-focused groups.

---

### 10. Automatic persistence
Group definitions are stored automatically.

What it does:

- no separate export step is required for normal use
- groups are reloaded when the tool window is opened again

Storage location:

- `%AppData%\SSMS.ObjectAggregator\groups.json`

## Example workflow

### Example 1: collect related objects across environments
Goal: find all customer-related objects in Dev, QA, and Prod.

1. Open __Tools > Object Aggregator__
2. Add group `Customer Objects`
3. Add instances:
   - `SQLDEV01 / SalesDb`
   - `SQLQA01 / SalesDb`
   - `SQLPROD01 / SalesDb`
4. For each instance, add filters:
   - Schema: `dbo`, Object: `Customer*`
   - Schema: `dbo`, Object: `usp*Customer*`
5. Expand the group or click **Reload Group**
6. Use the quick search box with `cust`
7. Double-click an object to locate it in Object Explorer

### Example 2: review ETL assets
Goal: see jobs and packages for one integration server.

1. Add group `ETL Review`
2. Add instance `SQLETL01 / SSIS`
3. Add filters such as:
   - Schema: `Finance`, Object: `Load*`
   - Schema: blank, Object: `Nightly*`
4. Reload the group
5. Browse:
   - `SQL Agent Jobs`
   - `SSIS Packages`

## Summary

`SSMS Object Aggregator` turns SSMS 22 into a lightweight cross-instance object browser.

It is most useful when you need to:

- aggregate object lists from multiple servers or databases
- keep reusable search definitions
- filter by schema and object naming patterns
- quickly narrow results in the UI
- jump back into **Object Explorer** for the selected object
- include SQL Agent and SSIS assets in the same workflow
