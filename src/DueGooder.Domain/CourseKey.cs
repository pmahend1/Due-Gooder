namespace DueGooder.Domain;

/// <summary>Natural key of a catalog course: school + subject + course number, e.g. <c>uky / CS / 115</c>.</summary>
public readonly record struct CourseKey(string SchoolId, string Subject, string CourseNumber);
