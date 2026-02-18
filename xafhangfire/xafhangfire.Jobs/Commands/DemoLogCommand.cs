namespace xafhangfire.Jobs.Commands;

public record DemoLogCommand(string Message, int DelaySeconds = 3);
