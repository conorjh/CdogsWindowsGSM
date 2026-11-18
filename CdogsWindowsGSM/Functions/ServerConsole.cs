using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;

namespace WindowsGSM.Functions
{
    public class ServerConsole
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateConsoleInputFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleInputW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WriteConsoleInput(
            SafeFileHandle consoleInput,
            [In] ConsoleInputRecord[] inputRecords,
            uint length,
            out uint eventsWritten);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_CHAR = 0x0102;
        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;
        private const ushort KeyEvent = 0x0001;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const int ErrorAccessDenied = 5;
        private const int ErrorInvalidHandle = 6;
        private const int ErrorInvalidParameter = 87;
        private const uint CreateNewConsole = 0x00000010;
        private const uint StartfUseShowWindow = 0x00000001;
        private const uint WaitObject0 = 0x00000000;
        private static readonly uint Infinite = uint.MaxValue;
        private static readonly object ConsoleInputLock = new object();
        private static readonly ConditionalWeakTable<Process, ProcessRuntimeMetadata> ProcessMetadataByProcess = new ConditionalWeakTable<Process, ProcessRuntimeMetadata>();

        private sealed class ProcessRuntimeMetadata
        {
            public ProcessStartInfo StartInfo { get; }
            public bool IsConsoleProcess { get; set; }
            public IntPtr ConsoleWindowHandle { get; set; }

            public ProcessRuntimeMetadata(ProcessStartInfo startInfo, bool isConsoleProcess)
            {
                StartInfo = startInfo;
                IsConsoleProcess = isConsoleProcess;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint Size;
            public string Reserved;
            public string Desktop;
            public string Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort ReservedCount;
            public IntPtr ReservedInfo;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ConsoleInputRecord
        {
            public ushort EventType;
            public ConsoleKeyEventRecord KeyEvent;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ConsoleKeyEventRecord
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool KeyDown;

            public ushort RepeatCount;
            public ushort VirtualKeyCode;
            public ushort VirtualScanCode;
            public char UnicodeChar;
            public uint ControlKeyState;
        }

        private const int MAX_LINE = 150;
        private readonly List<string> _consoleList = new List<string>();
        private readonly string _serverId;
        private int _lineNumber = 0;

        public static bool IsStandardInputRedirected(Process process)
        {
            return GetStartInfoOrDefault(process).RedirectStandardInput;
        }

        public static bool HasProcessStartInfo(Process process)
        {
            if (process == null)
            {
                return false;
            }

            if (ProcessMetadataByProcess.TryGetValue(process, out ProcessRuntimeMetadata metadata) && metadata.StartInfo != null)
            {
                return true;
            }

            try
            {
                _ = process.StartInfo;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static bool IsConsoleProcess(Process process)
        {
            return process != null &&
                ProcessMetadataByProcess.TryGetValue(process, out ProcessRuntimeMetadata metadata) &&
                metadata.IsConsoleProcess;
        }

        public static IntPtr GetMainWindowHandle(Process process)
        {
            if (process == null)
            {
                return IntPtr.Zero;
            }

            try
            {
                if (process.HasExited) { return IntPtr.Zero; }
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }

            IntPtr mainWindow;
            try
            {
                mainWindow = process.MainWindowHandle;
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }
            if (mainWindow != IntPtr.Zero && IsWindow(mainWindow))
            {
                if (ProcessMetadataByProcess.TryGetValue(process, out ProcessRuntimeMetadata existingMetadata))
                {
                    existingMetadata.ConsoleWindowHandle = mainWindow;
                }

                return mainWindow;
            }

            if (ProcessMetadataByProcess.TryGetValue(process, out ProcessRuntimeMetadata metadata) &&
                metadata.ConsoleWindowHandle != IntPtr.Zero && IsWindow(metadata.ConsoleWindowHandle))
            {
                return metadata.ConsoleWindowHandle;
            }

            IntPtr consoleWindow = GetConsoleWindowForProcess(process);
            if (consoleWindow == IntPtr.Zero || !IsWindow(consoleWindow))
            {
                return IntPtr.Zero;
            }

            metadata = ProcessMetadataByProcess.GetValue(process, _ => new ProcessRuntimeMetadata(null, isConsoleProcess: true));
            metadata.IsConsoleProcess = true;
            metadata.ConsoleWindowHandle = consoleWindow;
            return consoleWindow;
        }

        public static void SendCommandToProcess(Process process, string command)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("A console command is required.", nameof(command));
            }

            if (process.HasExited)
            {
                return;
            }

            if (IsStandardInputRedirected(process))
            {
                process.StandardInput.WriteLine(command);
                process.StandardInput.Flush();
                return;
            }

            if (IsConsoleProcess(process))
            {
                SendConsoleKeystrokes(process, command + "\r");
                return;
            }

            IntPtr mainWindow = GetMainWindowHandle(process);
            if (!HasUsableWindow(process, mainWindow))
            {
                try
                {
                    SendConsoleKeystrokes(process, command + "\r");
                }
                catch (Win32Exception exception)
                {
                    throw new InvalidOperationException("The server process has no usable window or attachable console input.", exception);
                }

                return;
            }

            SendMessageToMainWindow(mainWindow, command);
        }

        public static void SendConsoleKeystrokes(Process process, string keystrokes)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (string.IsNullOrEmpty(keystrokes))
            {
                throw new ArgumentException("Console keystrokes are required.", nameof(keystrokes));
            }

            if (process.HasExited)
            {
                return;
            }

            lock (ConsoleInputLock)
            {
                bool attachedToTargetConsole = AttachConsole((uint)process.Id);
                uint restoreConsoleProcessId = 0;
                if (!attachedToTargetConsole)
                {
                    int attachError = Marshal.GetLastWin32Error();
                    uint[] currentConsoleProcessIds = GetCurrentConsoleProcessIds();
                    bool targetAlreadySharesConsole = Array.IndexOf(currentConsoleProcessIds, (uint)process.Id) >= 0;
                    if (targetAlreadySharesConsole)
                    {
                        attachedToTargetConsole = false;
                    }
                    else if (attachError == ErrorAccessDenied && currentConsoleProcessIds.Length > 0)
                    {
                        int currentProcessId = Process.GetCurrentProcess().Id;
                        restoreConsoleProcessId = currentConsoleProcessIds.FirstOrDefault(processId =>
                            processId != (uint)currentProcessId && processId != (uint)process.Id);

                        if (!FreeConsole())
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not detach from the current console.");
                        }

                        attachedToTargetConsole = AttachConsole((uint)process.Id);
                        if (!attachedToTargetConsole)
                        {
                            int targetAttachError = Marshal.GetLastWin32Error();
                            if (restoreConsoleProcessId != 0 && !AttachConsole(restoreConsoleProcessId))
                            {
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the original console after a failed server-console attach.");
                            }

                            restoreConsoleProcessId = 0;
                            throw new Win32Exception(targetAttachError, "Could not attach to the server process console.");
                        }
                    }
                    else
                    {
                        throw new Win32Exception(attachError, "Could not attach to the server process console.");
                    }
                }

                try
                {
                    using SafeFileHandle input = CreateConsoleInputFile(
                        "CONIN$",
                        GenericRead | GenericWrite,
                        FileShareRead | FileShareWrite,
                        IntPtr.Zero,
                        OpenExisting,
                        0,
                        IntPtr.Zero);
                    if (input.IsInvalid)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the server console input buffer.");
                    }

                    ConsoleInputRecord[] records = CreateConsoleInputRecords(keystrokes);
                    if (!WriteConsoleInput(input, records, (uint)records.Length, out uint eventsWritten))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write keystrokes to the server console input buffer.");
                    }

                    if (eventsWritten != records.Length)
                    {
                        throw new IOException($"Only {eventsWritten} of {records.Length} server console key events were written.");
                    }
                }
                finally
                {
                    if (attachedToTargetConsole)
                    {
                        FreeConsole();
                    }

                    if (restoreConsoleProcessId != 0 && !AttachConsole(restoreConsoleProcessId))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the original console after sending server input.");
                    }
                }
            }
        }

        public static Process StartInNewConsole(ProcessStartInfo startInfo)
        {
            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            if (string.IsNullOrWhiteSpace(startInfo.FileName))
            {
                throw new ArgumentException("An executable path is required.", nameof(startInfo));
            }

            if (startInfo.UseShellExecute || startInfo.CreateNoWindow || startInfo.RedirectStandardInput ||
                startInfo.RedirectStandardOutput || startInfo.RedirectStandardError)
            {
                throw new ArgumentException("A new console requires shell execution and standard-stream redirection to be disabled.", nameof(startInfo));
            }

            var commandLine = new StringBuilder("\"" + startInfo.FileName + "\"");
            if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
            {
                commandLine.Append(' ').Append(startInfo.Arguments);
            }

            var startupInfo = new StartupInfo
            {
                Size = (uint)Marshal.SizeOf<StartupInfo>(),
                Flags = StartfUseShowWindow,
                ShowWindow = (ushort)startInfo.WindowStyle
            };
            string workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                ? Environment.CurrentDirectory
                : startInfo.WorkingDirectory;

            if (!CreateProcess(
                startInfo.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                CreateNewConsole,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out ProcessInformation processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start '{startInfo.FileName}' in a new console.");
            }

            try
            {
                Process process = Process.GetProcessById((int)processInformation.ProcessId);
                ProcessMetadataByProcess.Add(process, new ProcessRuntimeMetadata(startInfo, isConsoleProcess: true));
                process.EnableRaisingEvents = true;
                return process;
            }
            catch (ArgumentException exception)
            {
                if (WaitForSingleObject(processInformation.Process, 0) != WaitObject0)
                {
                    TerminateProcess(processInformation.Process, 1);
                    WaitForSingleObject(processInformation.Process, 10_000);
                }

                throw new InvalidOperationException("The new-console process could not be attached to a Process handle.", exception);
            }
            finally
            {
                CloseHandle(processInformation.Thread);
                CloseHandle(processInformation.Process);
            }
        }

        private static uint[] GetCurrentConsoleProcessIds()
        {
            uint[] processIds = new uint[16];
            uint processCount = GetConsoleProcessList(processIds, (uint)processIds.Length);
            if (processCount == 0)
            {
                return Array.Empty<uint>();
            }

            if (processCount > processIds.Length)
            {
                processIds = new uint[(int)processCount];
                processCount = GetConsoleProcessList(processIds, (uint)processIds.Length);
            }

            Array.Resize(ref processIds, (int)Math.Min(processCount, (uint)processIds.Length));
            return processIds;
        }

        private static IntPtr GetConsoleWindowForProcess(Process process)
        {
            lock (ConsoleInputLock)
            {
                uint targetProcessId;
                try
                {
                    targetProcessId = (uint)process.Id;
                }
                catch (InvalidOperationException)
                {
                    return IntPtr.Zero;
                }
                bool attachedToTargetConsole = AttachConsole(targetProcessId);
                uint restoreConsoleProcessId = 0;
                if (!attachedToTargetConsole)
                {
                    int attachError = Marshal.GetLastWin32Error();
                    uint[] currentConsoleProcessIds = GetCurrentConsoleProcessIds();
                    bool targetAlreadySharesConsole = Array.IndexOf(currentConsoleProcessIds, targetProcessId) >= 0;
                    if (targetAlreadySharesConsole)
                    {
                        attachedToTargetConsole = false;
                    }
                    else if (attachError == ErrorAccessDenied && currentConsoleProcessIds.Length > 0)
                    {
                        int currentProcessId = Process.GetCurrentProcess().Id;
                        restoreConsoleProcessId = currentConsoleProcessIds.FirstOrDefault(processId =>
                            processId != (uint)currentProcessId && processId != targetProcessId);
                        if (restoreConsoleProcessId == 0)
                        {
                            return IntPtr.Zero;
                        }

                        if (!FreeConsole())
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not detach from the current console to inspect the server console window.");
                        }

                        attachedToTargetConsole = AttachConsole(targetProcessId);
                        if (!attachedToTargetConsole)
                        {
                            int targetAttachError = Marshal.GetLastWin32Error();
                            if (!AttachConsole(restoreConsoleProcessId))
                            {
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the original console after inspecting the server console window.");
                            }

                            restoreConsoleProcessId = 0;
                            if (targetAttachError == ErrorAccessDenied ||
                                targetAttachError == ErrorInvalidHandle ||
                                targetAttachError == ErrorInvalidParameter)
                            {
                                return IntPtr.Zero;
                            }

                            throw new Win32Exception(targetAttachError, "Could not attach to the server console to retrieve its window handle.");
                        }
                    }
                    else if (attachError == ErrorAccessDenied ||
                        attachError == ErrorInvalidHandle ||
                        attachError == ErrorInvalidParameter)
                    {
                        return IntPtr.Zero;
                    }
                    else
                    {
                        throw new Win32Exception(attachError, "Could not attach to the server console to retrieve its window handle.");
                    }
                }

                try
                {
                    IntPtr consoleWindow = GetConsoleWindow();
                    ProcessRuntimeMetadata metadata = ProcessMetadataByProcess.GetValue(
                        process,
                        _ => new ProcessRuntimeMetadata(null, isConsoleProcess: true));
                    metadata.IsConsoleProcess = true;
                    if (consoleWindow != IntPtr.Zero)
                    {
                        metadata.ConsoleWindowHandle = consoleWindow;
                    }

                    return consoleWindow;
                }
                finally
                {
                    if (attachedToTargetConsole && !FreeConsole())
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not detach from the server console after retrieving its window handle.");
                    }

                    if (restoreConsoleProcessId != 0 && !AttachConsole(restoreConsoleProcessId))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the original console after retrieving the server console window handle.");
                    }
                }
            }
        }

        private static ConsoleInputRecord[] CreateConsoleInputRecords(string keystrokes)
        {
            var records = new List<ConsoleInputRecord>(keystrokes.Length * 2);
            for (int i = 0; i < keystrokes.Length; i++)
            {
                char character = keystrokes[i];
                if (character == '\n' && i > 0 && keystrokes[i - 1] == '\r')
                {
                    continue;
                }

                ushort virtualKey = character switch
                {
                    '\r' or '\n' => 0x000D,
                    '\t' => 0x0009,
                    '\b' => 0x0008,
                    _ => char.IsLetter(character) ? char.ToUpperInvariant(character) : character
                };
                char keyCharacter = character == '\n' ? '\r' : character;
                records.Add(new ConsoleInputRecord
                {
                    EventType = KeyEvent,
                    KeyEvent = new ConsoleKeyEventRecord
                    {
                        KeyDown = true,
                        RepeatCount = 1,
                        VirtualKeyCode = virtualKey,
                        UnicodeChar = keyCharacter
                    }
                });
                records.Add(new ConsoleInputRecord
                {
                    EventType = KeyEvent,
                    KeyEvent = new ConsoleKeyEventRecord
                    {
                        RepeatCount = 1,
                        VirtualKeyCode = virtualKey
                    }
                });
            }

            return records.ToArray();
        }

        public static bool IsStandardOutputRedirected(Process process)
        {
            return GetStartInfoOrDefault(process).RedirectStandardOutput;
        }

        public static bool IsStandardErrorRedirected(Process process)
        {
            return GetStartInfoOrDefault(process).RedirectStandardError;
        }

        public static string GetWorkingDirectory(Process process)
        {
            return GetStartInfoOrDefault(process).WorkingDirectory;
        }

        public static bool IsCreatedWithoutWindow(Process process)
        {
            return GetStartInfoOrDefault(process).CreateNoWindow;
        }

        public static bool HasUsableWindow(Process process, IntPtr window)
        {
            if (process == null || process.HasExited || window == IntPtr.Zero || !IsWindow(window))
            {
                return false;
            }

            IntPtr processWindow = GetMainWindowHandle(process);
            return processWindow == IntPtr.Zero || processWindow == window;
        }

        private static ProcessStartInfo GetStartInfoOrDefault(Process process)
        {
            if (process == null)
            {
                return new ProcessStartInfo();
            }

            try
            {
                return process.StartInfo;
            }
            catch (InvalidOperationException)
            {
                if (ProcessMetadataByProcess.TryGetValue(process, out ProcessRuntimeMetadata metadata) && metadata.StartInfo != null)
                {
                    return metadata.StartInfo;
                }

                // .NET Framework exposed a default StartInfo for processes attached by PID.
                return new ProcessStartInfo();
            }
        }

        public ServerConsole(string serverId)
        {
            _serverId = serverId;
        }

        public ServerConsole(int serverId)
        {
            _serverId = serverId.ToString();
        }

        public async void AddOutput(object sender, DataReceivedEventArgs args)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                MainWindow._serverMetadata[int.Parse(_serverId)].ServerConsole.Add(args.Data);
            });
        }

        public async void Input(Process process, string text, IntPtr mainWindow)
        {
            if (!process.HasExited)
            {
                if (IsStandardInputRedirected(process))
                {
                    try
                    {
                        process.StandardInput.WriteLine(text);
                        Add(text);
                    }
                    catch
                    {
                        //ignore
                    }
                }
                else if (IsConsoleProcess(process))
                {
                    try
                    {
                        SendConsoleKeystrokes(process, text + "\r");
                        Add(text);
                    }
                    catch (Win32Exception exception)
                    {
                        Add($"[ERROR] Could not send command to the server console: {exception.Message}");
                    }
                    catch (IOException exception)
                    {
                        Add($"[ERROR] Could not send command to the server console: {exception.Message}");
                    }
                }
                else if (HasUsableWindow(process, mainWindow))
                {
                    await Task.Run(() =>
                    {
                        if (!process.HasExited && process.ProcessName == "7DaysToDieServer")
                        {
                            SetForegroundWindow(mainWindow);
                            var current = GetForegroundWindow();
                            var wgsmWindow = Process.GetCurrentProcess().MainWindowHandle;
                            if (current != wgsmWindow)
                            {
                                SendWaitToMainWindow("{TAB}");
                                SendWaitToMainWindow(text);
                                SendWaitToMainWindow("{TAB}");
                                SendWaitToMainWindow(text);
                                SendWaitToMainWindow("{ENTER}");
                                SetForegroundWindow(wgsmWindow);
                            }
                        }
                        else
                        {
                            SendMessageToMainWindow(mainWindow, text);
                        }
                    });
                }
            }
        }

        public void Clear()
        {
            _consoleList.Clear();
        }

        public string Get()
        {
            return string.Join(Environment.NewLine, _consoleList.ToArray());
        }

        public string GetPreviousCommand()
        {
            --_lineNumber;
            return (_consoleList.Count == 0) ? string.Empty : _consoleList[GetLineNumber()];
        }

        public string GetNextCommand()
        {
            ++_lineNumber;
            return (_consoleList.Count == 0) ? string.Empty : _consoleList[GetLineNumber()];
        }

        private int GetLineNumber()
        {
            if (_lineNumber < 0)
            {
                _lineNumber = 0;
            }
            else if (_lineNumber >= _consoleList.Count)
            {
                _lineNumber = (_consoleList.Count <= 0) ? 0 : _consoleList.Count - 1;
            }

            return _lineNumber;
        }

        public void Add(string text)
        {
            if (_serverId == "0")
            {
                _lineNumber = _consoleList.Count + 1;

                if (_consoleList.Count > 0 && text == _consoleList[_consoleList.Count - 1])
                {
                    return;
                }
            }

            _consoleList.Add(text);
            if (_consoleList.Count > MAX_LINE)
            {
                _consoleList.RemoveAt(0);
            }
        }

        public static void SendMessageToMainWindow(IntPtr hWnd, string message)
        {
            // Here is a minor error on PostMessage, when it sends repeated char, some char may disappear. Example: send 1111111, windows may receive 1111 or 11111
            for (int i = 0; i < message.Length; i++)
            {
                // This is the solution for the error stated above
                if (i > 0 && message[i] == message[i-1])
                {
                    // Send a None key, break the repeat bug
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)Keys.None, (IntPtr)0);
                }

                PostMessage(hWnd, WM_CHAR, (IntPtr)message[i], (IntPtr)0);
            }

            // Send enter
            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)Keys.Enter, (IntPtr)(0 << 29 | 0));
        }

        public static void SetMainWindow(IntPtr hWnd)
        {
            SetForegroundWindow(hWnd);
        }

        public static void SendWaitToMainWindow(string keys)
        {
            try
            {
                SendKeys.SendWait(keys);
            }
            catch
            {
                /*
                    System.ComponentModel.Win32Exception (0x80004005): Access is denied
                    at System.Windows.Forms.SendKeys.SendInput(Byte[] oldKeyboardState, Queue previousEvents)
                    at System.Windows.Forms.SendKeys.Send(String keys, Control control, Boolean wait)
                    at System.Windows.Forms.SendKeys.SendWait(String keys)

                    This error may happen in Windows Server R2, UAC problem, not sure how to fix

                    https://github.com/WindowsGSM/WindowsGSM/issues/14
                */
            }
        }
    }
}
