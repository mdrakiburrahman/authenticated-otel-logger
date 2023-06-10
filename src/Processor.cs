using OpenTelemetry;
using OpenTelemetry.Logs;

public class TestProcessor : BaseProcessor<LogRecord>
{
    private readonly string name;

    public TestProcessor(string name = "MyProcessor")
    {
        this.name = name;
    }

    public override void OnEnd(LogRecord record)
    {
        Console.WriteLine($"{this.name}.OnEnd({record})");
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        Console.WriteLine($"{this.name}.OnForceFlush({timeoutMilliseconds})");
        return true;
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        Console.WriteLine($"{this.name}.OnShutdown({timeoutMilliseconds})");
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Console.WriteLine($"{this.name}.Dispose({disposing})");
    }

}