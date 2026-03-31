namespace SSMS.ObjectAggregator.Utilities;

internal static class ObjectTypeDisplayHelper
{
    private static readonly Dictionary<string, (string Display, int Order)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        { "USER_TABLE",                          ("Tables",                          0) },
        { "SQL_STORED_PROCEDURE",                ("Stored Procedures",               1) },
        { "CLR_STORED_PROCEDURE",                ("Stored Procedures (CLR)",         2) },
        { "EXTENDED_STORED_PROCEDURE",           ("Extended Stored Procedures",      3) },
        { "VIEW",                                ("Views",                           4) },
        { "SQL_TABLE_VALUED_FUNCTION",           ("Table-valued Functions",          5) },
        { "SQL_INLINE_TABLE_VALUED_FUNCTION",    ("Inline Table-valued Functions",   6) },
        { "SQL_SCALAR_FUNCTION",                 ("Scalar Functions",                7) },
        { "CLR_SCALAR_FUNCTION",                 ("CLR Functions",                   8) },
        { "CLR_TABLE_VALUED_FUNCTION",           ("CLR Table-valued Functions",      9) },
        { "CLR_AGGREGATE_FUNCTION",              ("CLR Aggregate Functions",         10) },
        { "SYNONYM",                             ("Synonyms",                        11) },
        { "SERVICE_QUEUE",                       ("Service Queues",                  12) },
        { "SEQUENCE_OBJECT",                     ("Sequences",                       13) },
        { "RULE",                                ("Rules",                           14) },
        { "TYPE_TABLE",                          ("User-Defined Table Types",        15) },
        { "USER_DEFINED_TYPE",                   ("User-Defined Data Types",         16) },
        { "XML_SCHEMA_COLLECTION",               ("XML Schema Collections",          17) },
        { "SQL_TRIGGER",                         ("DML Triggers",                    18) },
        { "CLR_TRIGGER",                         ("DML Triggers (CLR)",              19) },
        { "DATABASE_DDL_TRIGGER",                ("DDL Triggers",                    20) },
        { "CHECK_CONSTRAINT",                    ("Check Constraints",               21) },
        { "DEFAULT_CONSTRAINT",                  ("Default Constraints",             22) },
        { "FOREIGN_KEY_CONSTRAINT",              ("Foreign Keys",                    23) },
        { "PRIMARY_KEY_CONSTRAINT",              ("Primary Key Constraints",         24) },
        { "UNIQUE_CONSTRAINT",                   ("Unique Constraints",              25) },
        { "SQL_AGENT_JOB",                       ("SQL Agent Jobs",                  50) },
        { "SSIS_PACKAGE",                        ("SSIS Packages",                   51) },
    };

    public static string GetDisplayName(string? typeDesc)
    {
        if (string.IsNullOrWhiteSpace(typeDesc))
            return "Other";

        if (Map.TryGetValue(typeDesc!, out var tuple))
            return tuple.Display;

        string text = typeDesc!.Replace('_', ' ').ToLowerInvariant();
        if (text.Length == 0)
        {
            return "Other";
        }

        return char.ToUpperInvariant(text[0]) + (text.Length > 1 ? text.Substring(1) : string.Empty);
    }

    public static int GetOrder(string? typeDesc)
    {
        if (!string.IsNullOrWhiteSpace(typeDesc) && Map.TryGetValue(typeDesc!, out var tuple))
        {
            return tuple.Order;
        }

        return int.MaxValue - 1;
    }
}