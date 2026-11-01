using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>One call on the path to a toast, and what it answered.</summary>
public sealed record ToastCall(string Step, int Hr);

/// <summary>
/// A toast with every call on the path to Show made, as the toast host drives it: the seam a test drives with a fake,
/// so the order of Show and the Setting is held without touching Windows.
/// </summary>
public interface IPreparedToast : IDisposable
{
    IReadOnlyList<ToastCall> Calls { get; }

    string? FailedStep { get; }

    int Show();

    (int Hr, int? Value) ReadSetting();
}

/// <summary>
/// The toast, through a hand-written interop over <c>combase.dll</c>'s WinRT activation: no projection, no Windows-only
/// target framework, no package. It runs only in the <c>toast-host</c> child, never in the runtime: a wrong vtable slot
/// is an access violation .NET cannot catch, and here it ends a child that lives a fraction of a second rather than the
/// runtime and every attempt it is watching.
/// </summary>
/// <remarks>
/// <para>
/// Every interface below derives from IInspectable, so its own methods start at slot 6 (IUnknown 0–2, IInspectable
/// 3–5). The slots and interface ids are the ones measured on real Windows accounts: <c>IXmlDocumentIO.LoadXml</c> 6,
/// <c>IToastNotificationFactory.CreateToastNotification</c> 6, <c>IToastNotificationManagerStatics.CreateToastNotifierWithId</c>
/// 7, <c>IToastNotifier.Show</c> 6 and <c>get_Setting</c> 8.
/// </para>
/// <para>
/// The notifier's Setting is read after Show and never before, and never gates it: before an AppUserModelID's first
/// toast Windows has no entry for it and answers <c>0x80070490</c>, and Show returns success even while the person has
/// switched Jason off — which only the Setting read after it can tell.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsToast
{
    private static readonly Guid IidXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static readonly Guid IidXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static readonly Guid IidToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid IidToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");

    private const string ClassXmlDocument = "Windows.Data.Xml.Dom.XmlDocument";
    private const string ClassToastNotification = "Windows.UI.Notifications.ToastNotification";
    private const string ClassToastNotificationManager = "Windows.UI.Notifications.ToastNotificationManager";

    private const int RoInitMultithreaded = 1;

    /// <summary>A toast of a title and lines of text, each escaped as XML text.</summary>
    public static string Xml(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        var texts = string.Concat(lines.Select(line => $"<text>{SecurityElement.Escape(line)}</text>"));
        return "<toast><visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(title)}</text>{texts}"
            + "</binding></visual></toast>";
    }

    /// <summary>An HRESULT as it is written down: <c>0x80070490</c>.</summary>
    public static string Hr(int hr) => "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>
    /// Every call on the path to Show, for this AppUserModelID: the XML loaded, the notification made, the notifier made.
    /// A call that fails ends the sequence and names itself. Nothing is shown and nothing is written.
    /// </summary>
    public static PreparedToast Prepare(string aumid, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aumid);
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var prepared = new PreparedToast();
        var initialized = RoInitialize(RoInitMultithreaded);
        prepared.Initialized = initialized >= 0;

        try
        {
            var classXml = prepared.String(ClassXmlDocument);
            var classToast = prepared.String(ClassToastNotification);
            var classManager = prepared.String(ClassToastNotificationManager);
            var id = prepared.String(aumid);
            var text = prepared.String(xml);

            IntPtr document;
            if (!prepared.Step("RoActivateInstance(XmlDocument)", RoActivateInstance(classXml, &document)))
            {
                return prepared;
            }

            prepared.Keep(document);

            if (!prepared.Query(document, IidXmlDocumentIO, "QueryInterface(IXmlDocumentIO)", out var documentIO)
                || !prepared.Step("IXmlDocumentIO.LoadXml", ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(documentIO, 6))(documentIO, text))
                || !prepared.Query(document, IidXmlDocument, "QueryInterface(IXmlDocument)", out var xmlDocument)
                || !prepared.Factory(classToast, IidToastNotificationFactory, "RoGetActivationFactory(ToastNotification)", out var toastFactory))
            {
                return prepared;
            }

            IntPtr notification;
            if (!prepared.Step(
                    "IToastNotificationFactory.CreateToastNotification",
                    ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(toastFactory, 6))(toastFactory, xmlDocument, &notification)))
            {
                return prepared;
            }

            prepared.Keep(notification);

            if (!prepared.Factory(classManager, IidToastNotificationManagerStatics, "RoGetActivationFactory(ToastNotificationManager)", out var manager))
            {
                return prepared;
            }

            IntPtr notifier;
            if (!prepared.Step(
                    "IToastNotificationManagerStatics.CreateToastNotifierWithId",
                    ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(manager, 7))(manager, id, &notifier)))
            {
                return prepared;
            }

            prepared.Keep(notifier);
            prepared.Ready(notification, notifier);
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    internal static IntPtr Slot(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

    internal static int QueryRaw(IntPtr from, Guid iid, out IntPtr result)
    {
        IntPtr found;
        var hr = ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(from, 0))(from, &iid, &found);
        result = hr >= 0 ? found : IntPtr.Zero;
        return hr;
    }

    internal static int Factory(IntPtr classId, Guid iid, out IntPtr factory)
    {
        IntPtr found;
        var hr = RoGetActivationFactory(classId, &iid, &found);
        factory = hr >= 0 ? found : IntPtr.Zero;
        return hr;
    }

    internal static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            ((delegate* unmanaged<IntPtr, uint>)Slot(instance, 2))(instance);
        }
    }

    [LibraryImport("combase.dll")]
    private static partial int RoInitialize(int initType);

    [LibraryImport("combase.dll")]
    internal static partial void RoUninitialize();

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(IntPtr activatableClassId, IntPtr* instance);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(IntPtr activatableClassId, Guid* iid, IntPtr* factory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WindowsCreateString(string source, int length, out IntPtr text);

    [LibraryImport("combase.dll")]
    internal static partial int WindowsDeleteString(IntPtr text);
}

/// <summary>
/// A toast with every call on the path to Show made: what each answered, which one failed if any, and — only where none
/// did — Show and the Setting read after it. Disposing releases every interface and deletes every string.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class PreparedToast : IPreparedToast
{
    private readonly List<ToastCall> _calls = [];
    private readonly List<IntPtr> _objects = [];
    private readonly List<IntPtr> _strings = [];
    private IntPtr _notification;
    private IntPtr _notifier;
    private bool _disposed;

    internal PreparedToast()
    {
    }

    public IReadOnlyList<ToastCall> Calls => _calls;

    /// <summary>The call that failed on the path to Show, or null when every one answered.</summary>
    public string? FailedStep { get; private set; }

    internal bool Initialized { get; set; }

    /// <summary>Shows the toast; its HRESULT, whatever it is.</summary>
    /// <exception cref="InvalidOperationException">A call on the path to Show failed, so there is nothing to show.</exception>
    public int Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifier == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Nothing to show: {FailedStep} failed.");
        }

        return ((delegate* unmanaged<IntPtr, IntPtr, int>)WindowsToast.Slot(_notifier, 6))(_notifier, _notification);
    }

    /// <summary>The notifier's Setting: its HRESULT, and the value only where it was read. Read after Show, never before.</summary>
    public (int Hr, int? Value) ReadSetting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifier == IntPtr.Zero)
        {
            throw new InvalidOperationException($"No notifier: {FailedStep} failed.");
        }

        int setting;
        var hr = ((delegate* unmanaged<IntPtr, int*, int>)WindowsToast.Slot(_notifier, 8))(_notifier, &setting);
        return (hr, hr >= 0 ? setting : null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _objects.Count - 1; index >= 0; index--)
        {
            WindowsToast.Release(_objects[index]);
        }

        foreach (var text in _strings)
        {
            _ = WindowsToast.WindowsDeleteString(text);
        }

        if (Initialized)
        {
            WindowsToast.RoUninitialize();
        }
    }

    internal IntPtr String(string value)
    {
        var hr = WindowsToast.WindowsCreateString(value, value.Length, out var text);
        if (hr < 0)
        {
            throw new InvalidOperationException($"WindowsCreateString failed: {WindowsToast.Hr(hr)}");
        }

        _strings.Add(text);
        return text;
    }

    internal void Keep(IntPtr instance) => _objects.Add(instance);

    internal bool Step(string name, int hr)
    {
        _calls.Add(new ToastCall(name, hr));
        if (hr >= 0)
        {
            return true;
        }

        FailedStep = name;
        return false;
    }

    internal bool Query(IntPtr from, Guid iid, string name, out IntPtr result)
    {
        var hr = WindowsToast.QueryRaw(from, iid, out result);
        if (hr >= 0)
        {
            Keep(result);
        }

        return Step(name, hr);
    }

    internal bool Factory(IntPtr classId, Guid iid, string name, out IntPtr result)
    {
        var hr = WindowsToast.Factory(classId, iid, out result);
        if (hr >= 0)
        {
            Keep(result);
        }

        return Step(name, hr);
    }

    internal void Ready(IntPtr notification, IntPtr notifier)
    {
        _notification = notification;
        _notifier = notifier;
    }
}
