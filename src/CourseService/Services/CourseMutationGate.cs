namespace CourseService.Services;

// EF InMemory does not enforce relational uniqueness/transactions. A process-wide
// gate makes enrollment and deletion atomic within this single-instance POC.
public sealed class CourseMutationGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public void Dispose() => Semaphore.Dispose();
}
