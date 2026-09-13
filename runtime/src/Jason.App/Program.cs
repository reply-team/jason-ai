using Jason.Contracts;

if (args is ["--version"])
{
    Console.Out.WriteLine(JasonVersion.Current);
    return 0;
}

Console.Error.WriteLine("jason: the command-line interface is not wired yet in this build.");
return 2;
