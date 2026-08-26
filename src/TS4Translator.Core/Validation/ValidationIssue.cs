namespace TS4Translator.Core.Validation;

public sealed record ValidationIssue(ValidationIssueCode Code, ValidationSeverity Severity, string Message);
