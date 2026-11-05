namespace OpenCodeManager.Worker;
public sealed class ManagerAlreadyRunningException(Exception inner) : Exception("Another manager instance holds the database lock", inner);
