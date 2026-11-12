using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jason.App;
using Jason.DyingRuntime;

// The shipped executable's modes, routed by the shipped router. Only a runtime gets the thread that dies: in any other
// mode this is the executable as it ships.
if (ModeRouter.Select(args) == Mode.RuntimeService)
{
    Planted.Arm(args);
}

return await ModeRouter.RunAsync(args);

namespace Jason.DyingRuntime
{
    /// <summary>The failure this program plants, and what its message says: text the runtime's log must never carry.</summary>
    public static class Planted
    {
        /// <summary>
        /// The message the planted exception carries: a sentinel, a path and a sentence, the three things a message can
        /// carry that a log line may not.
        /// </summary>
        public const string Message = "SENTINEL_DEATH_MESSAGE planted at C:\\made-up\\folder\\notes.txt: a sentence somebody typed";

        /// <summary>The method that throws, by the name a stack frame gives it.</summary>
        public const string Thrower = "Jason.DyingRuntime.Planted.Die";

        /// <summary>
        /// A thread that waits for the runtime to publish its descriptor — by then it has its log, its API and its
        /// services — and then throws with nothing to catch it. On Windows the process first says it wants no error box,
        /// so a run on a desktop never waits on a window nobody will close.
        /// </summary>
        internal static void Arm(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox);
            }

            var descriptor = ModeRouter.PathsFor(RuntimeRunArguments.Parse(args)).DescriptorFile;
            var thread = new Thread(() =>
            {
                // Waiting on the descriptor, the runtime's own sign that it has started; a runtime that never publishes
                // one is the test's to end, and this thread goes with it.
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (!File.Exists(descriptor))
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        return;
                    }

                    Thread.Sleep(50);
                }

                Die();
            })
            {
                IsBackground = true,
                Name = "planted failure",
            };
            thread.Start();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Die() => throw new PlantedFailureException(Message);

        private const uint SemFailCriticalErrors = 0x0001;
        private const uint SemNoGpFaultErrorBox = 0x0002;

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint SetErrorMode(uint mode);
    }

    /// <summary>The planted failure's own type, so the line can be held to naming exactly it.</summary>
    public sealed class PlantedFailureException : Exception
    {
        public PlantedFailureException()
        {
        }

        public PlantedFailureException(string message)
            : base(message)
        {
        }

        public PlantedFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
