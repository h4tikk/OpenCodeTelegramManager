namespace OpenCodeManager.Application;
public sealed record SessionTime(double Created, double Updated, double? Busy, double? Idle, double? Archived);
