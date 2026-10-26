using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

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
        private static readonly object ConsoleInputLock = new object();

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

            IntPtr mainWindow = process.MainWindowHandle;
            if (!HasUsableWindow(process, mainWindow))
            {
                throw new InvalidOperationException("The server process has neither redirected standard input nor a usable console window.");
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
                if (!attachedToTargetConsole &&
                    (Marshal.GetLastWin32Error() != ErrorAccessDenied || !IsProcessAttachedToCurrentConsole(process.Id)))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach to the server process console.");
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
                }
            }
        }

        private static bool IsProcessAttachedToCurrentConsole(int processId)
        {
            uint[] processIds = new uint[16];
            uint processCount = GetConsoleProcessList(processIds, (uint)processIds.Length);
            if (processCount == 0)
            {
                return false;
            }

            if (processCount > processIds.Length)
            {
                processIds = new uint[processCount];
                processCount = GetConsoleProcessList(processIds, (uint)processIds.Length);
            }

            int count = (int)Math.Min(processCount, (uint)processIds.Length);
            for (int i = 0; i < count; i++)
            {
                if (processIds[i] == (uint)processId)
                {
                    return true;
                }
            }

            return false;
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

            IntPtr processWindow = process.MainWindowHandle;
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
