// WoW Stick Bridge
// Maps keyboard movement keys to a virtual Xbox 360 controller's left stick.
// By default only S (down) is mapped; --forward adds W, --strafe adds A/D,
// --wasd adds all of them. WoW Forever's native gamepad movement then turns
// the character toward the movement direction and runs at full speed.
//
// Requires: ViGEmBus driver + ViGEmClient.dll next to this exe.
// Build (no .NET SDK needed, C# 5 / .NET Framework):
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe
//     /platform:x64 /optimize+ /out:WoWStickBridge.exe WoWStickBridge.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WoWStickBridge
{
    internal static class Program
    {
        // ---------------------------------------------------------------- ViGEm
        private const string ViGEmDll = "ViGEmClient.dll";
        private const int VIGEM_ERROR_NONE = 0x20000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct XUSB_REPORT
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr vigem_alloc();

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vigem_free(IntPtr client);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vigem_connect(IntPtr client);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vigem_disconnect(IntPtr client);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr vigem_target_x360_alloc();

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vigem_target_free(IntPtr target);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vigem_target_add(IntPtr client, IntPtr target);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vigem_target_remove(IntPtr client, IntPtr target);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vigem_target_x360_update(IntPtr client, IntPtr target, XUSB_REPORT report);

        [DllImport(ViGEmDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vigem_target_x360_get_user_index(IntPtr client, IntPtr target, out uint index);

        // ------------------------------------------------------------- keyboard
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_KEYUP = 0x0101;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_QUIT = 0x0012;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int HOTKEY_QUIT = 0xA001;
        private const int HOTKEY_TOGGLE = 0xA002;
        private const int HOTKEY_NUDGE = 0xA003;
        private const int HOTKEY_XMODE = 0xA004;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
            public uint lPrivate;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // ---------------------------------------------------------------- state
        private static readonly object PadLock = new object();

        private static IntPtr client = IntPtr.Zero;
        private static IntPtr target = IntPtr.Zero;
        private static bool targetAttached;
        private static short appliedX;
        private static short appliedY;

        // keyboard key -> stick deflection
        private sealed class KeyMove
        {
            public int Vk;
            public string Name;
            public short X;
            public short Y;
            public bool Held;
        }

        private static readonly List<KeyMove> moves = new List<KeyMove>();

        private static int keyVk = 0x53;          // S: the backward key (--key)
        private static int maxStick = 32767;      // stick magnitude
        private static bool invertY;              // --invert
        private static bool onlyInWow = true;     // --no-focus disables
        private static bool onDemand;             // --ondemand
        private static bool enabled = true;       // Ctrl+Alt+S toggles
        private static string[] wowProcesses = new string[] { "wow" };
        private static int removeDueMs = -1;
        private static bool neutralPending;
        private static int neutralTries;
        private static int neutralDueMs = -1;
        private static bool debug;
        private static bool swallow = true;   // on by default: hide mapped keys from the game
        private const string Version = "1.7";
        private static int xMode = 0;             // 0 normal (default), 1 flip always, 2 flip when S, 3 flip when W
        private static bool altToggle = true;     // tap Alt (without other keys) toggles translation
        private static bool altDown;
        private static int altDownMs;
        private static bool altOtherKey;
        private static uint padIndex;
        private static bool padIndexKnown;
        private static int nudgeUntil = -1;
        private static readonly string LogPath = ChooseLogPath();

        private static string ChooseLogPath()
        {
            // Prefer the exe folder (portable / project use). If it is not
            // writable (e.g. installed into Program Files), fall back to
            // %LOCALAPPDATA%\WoW Stick Bridge\bridge.log.
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge.log");
                using (FileStream fs = File.Open(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
                return p;
            }
            catch { }
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoW Stick Bridge");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "bridge.log");
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge.log");
        }

        private static IntPtr hook = IntPtr.Zero;
        private static Timer ticker;
        private static LowLevelKeyboardProc hookProc; // keep the delegate alive

        private static uint lastPid;
        private static string lastProcessName = "";

        // ------------------------------------------------------------- helpers
        private static void Log(string text)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text;
            Console.WriteLine(line);
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { }
        }

        private static string ErrorName(int code)
        {
            uint c = (uint)code;
            if (c == 0xE0000001) return "BUS_NOT_FOUND (ViGEmBus driver is not installed / not running)";
            if (c == 0xE0000002) return "NO_FREE_SLOT (all virtual pad slots are busy, reboot recommended)";
            if (c == 0xE0000003) return "INVALID_TARGET";
            if (c == 0xE0000004) return "REMOVAL_FAILED";
            if (c == 0xE0000005) return "ALREADY_CONNECTED";
            if (c == 0xE0000006) return "TARGET_UNINITIALIZED";
            if (c == 0xE0000007) return "TARGET_NOT_PLUGGED_IN";
            if (c == 0xE0000008) return "BUS_VERSION_MISMATCH (ViGEmBus driver too old)";
            if (c == 0xE0000009) return "BUS_ACCESS_FAILED";
            if (c == 0xE0000014) return "XUSB_USERINDEX_OUT_OF_RANGE";
            if (c == 0xE0000016) return "NOT_SUPPORTED";
            if (c == 0xE0000017) return "WINAPI";
            if (c == 0xE0000018) return "TIMED_OUT";
            return "0x" + code.ToString("X8");
        }

        private static bool IsSuccess(int code)
        {
            return code == VIGEM_ERROR_NONE;
        }

        private static string Normalize(string name)
        {
            if (name == null) return "";
            name = name.Trim().ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            return name;
        }

        private static bool IsWowName(string name)
        {
            string n = Normalize(name);
            if (n.Length == 0) return false;
            for (int i = 0; i < wowProcesses.Length; i++)
            {
                if (Normalize(wowProcesses[i]) == n) return true;
            }
            return false;
        }

        private static string ForegroundProcessName()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return "";
            if (pid != lastPid)
            {
                lastPid = pid;
                string name = "";
                try
                {
                    Process p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                }
                catch
                {
                    name = "";
                }
                lastProcessName = name;
            }
            return lastProcessName;
        }

        private static bool WowFocused()
        {
            if (!onlyInWow) return true;
            return IsWowName(ForegroundProcessName());
        }

        private static bool AltPhysicalDown()
        {
            return ((GetAsyncKeyState(0x12) & 0x8000) != 0)
                || ((GetAsyncKeyState(0xA4) & 0x8000) != 0)
                || ((GetAsyncKeyState(0xA5) & 0x8000) != 0);
        }

        // A low-level hook usually reports the Alt key-up, but some windows
        // (menus, terminals) swallow it. Recover it from the real key state so
        // a short Alt tap always toggles the chat pause exactly once.
        private static void RecoverAltStateLocked()
        {
            if (!altDown) return;
            if (AltPhysicalDown()) return;
            altDown = false;
            int heldFor = unchecked(Environment.TickCount - altDownMs);
            if (!altOtherKey && heldFor < 600) ToggleTranslation();
        }

        private static string XModeName(int mode)
        {
            if (mode == 0) return "normal";
            if (mode == 1) return "flip always";
            if (mode == 2) return "flip when S";
            return "flip when W";
        }

        private static bool CtrlOrAltDown()
        {
            return (GetAsyncKeyState(0x11) & 0x8000) != 0
                || (GetAsyncKeyState(0x12) & 0x8000) != 0;
        }

        private static string KeyName(int vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            return "0x" + vk.ToString("X2");
        }

        private static short DownValue()
        {
            return (short)(invertY ? maxStick : -maxStick);
        }

        private static short UpValue()
        {
            return (short)(-DownValue());
        }

        private static void BuildMoves(bool forward, bool strafe)
        {
            moves.Clear();
            moves.Add(NewMove(keyVk, KeyName(keyVk), 0, DownValue()));
            if (forward) moves.Add(NewMove(0x57, "W", 0, UpValue()));
            if (strafe)
            {
                moves.Add(NewMove(0x41, "A", (short)(-maxStick), 0));
                moves.Add(NewMove(0x44, "D", (short)maxStick, 0));
            }
        }

        private static KeyMove NewMove(int vk, string name, short x, short y)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Vk == vk) return moves[i];
            }
            KeyMove m = new KeyMove();
            m.Vk = vk;
            m.Name = name;
            m.X = x;
            m.Y = y;
            return m;
        }

        private static KeyMove FindMove(uint vk)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                if ((uint)moves[i].Vk == vk) return moves[i];
            }
            return null;
        }

        private static bool HeldAny()
        {
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Held) return true;
            }
            return false;
        }

        // Sums the held directions, normalizing so the stick never exceeds
        // full deflection (W+D is a diagonal, not two full pushes).
        private static bool ComputeVector(out short dx, out short dy)
        {
            int sx = 0;
            int sy = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Held)
                {
                    sx += moves[i].X;
                    sy += moves[i].Y;
                }
            }
            dx = 0;
            dy = 0;
            if (sx == 0 && sy == 0) return false;
            bool flip = (xMode == 1) || (xMode == 2 && sy < 0) || (xMode == 3 && sy > 0);
            if (flip) sx = -sx;
            double len = Math.Sqrt((double)sx * (double)sx + (double)sy * (double)sy);
            if (len > maxStick)
            {
                sx = (int)Math.Round((double)sx * maxStick / len);
                sy = (int)Math.Round((double)sy * maxStick / len);
            }
            dx = (short)sx;
            dy = (short)sy;
            return true;
        }

        // ------------------------------------------------- XInput (read-back only)
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        private static string PadReadback()
        {
            if (!padIndexKnown) return "?";
            try
            {
                XINPUT_STATE st;
                if (XInputGetState(padIndex, out st) == 0)
                {
                    return st.Gamepad.sThumbLX + "," + st.Gamepad.sThumbLY;
                }
            }
            catch { }
            return "err";
        }

        // A 3-second forced stick deflection, started with Ctrl+Alt+D: lets us
        // see whether the game keeps moving with a sustained virtual stick.
        private static bool NudgeActiveLocked()
        {
            if (nudgeUntil < 0) return false;
            if (Environment.TickCount < nudgeUntil) return true;
            nudgeUntil = -1;
            Log("NUDGE end.");
            return false;
        }

        // ------------------------------------------------------------- ViGEm pad
        private static bool EnsurePadLocked()
        {
            if (targetAttached) return true;

            IntPtr t = vigem_target_x360_alloc();
            if (t == IntPtr.Zero)
            {
                Log("ERROR: vigem_target_x360_alloc() returned NULL.");
                return false;
            }

            int err = vigem_target_add(client, t);
            if (!IsSuccess(err))
            {
                Log("ERROR: vigem_target_add() -> " + ErrorName(err));
                vigem_target_free(t);
                return false;
            }

            target = t;
            targetAttached = true;

            // A freshly plugged ViGEm device starts with an uninitialized report
            // and can swallow the very first (all-zero) update, so force a tiny
            // non-zero deflection first, then the neutral report.
            appliedX = 1;
            appliedY = 1;
            SendStickLocked(0, 64);
            SendStickLocked(0, 0);
            neutralPending = true;
            neutralTries = 0;
            neutralDueMs = Environment.TickCount + 400;

            uint index;
            int idxErr = vigem_target_x360_get_user_index(client, target, out index);
            if (IsSuccess(idxErr))
            {
                padIndex = index;
                padIndexKnown = true;
                Log("Virtual Xbox 360 pad connected (XInput slot " + index + ").");
            }
            else
            {
                Log("Virtual Xbox 360 pad connected.");
            }
            return true;
        }

        private static void RemovePadLocked()
        {
            if (!targetAttached) return;
            int err = vigem_target_remove(client, target);
            if (!IsSuccess(err)) Log("WARN: vigem_target_remove() -> " + ErrorName(err));
            vigem_target_free(target);
            target = IntPtr.Zero;
            targetAttached = false;
            appliedX = 0;
            appliedY = 0;
            removeDueMs = -1;
            neutralPending = false;
            neutralDueMs = -1;
        }

        private static void SendStickLocked(short x, short y)
        {
            if (!targetAttached) return;
            if (x == appliedX && y == appliedY) return;

            XUSB_REPORT report = new XUSB_REPORT();
            report.sThumbLX = x;
            report.sThumbLY = y;
            report.sThumbRX = 0;
            report.sThumbRY = 0;
            report.bLeftTrigger = 0;
            report.bRightTrigger = 0;
            report.wButtons = 0;

            int err = vigem_target_x360_update(client, target, report);
            if (!IsSuccess(err))
            {
                Log("ERROR: vigem_target_x360_update() -> " + ErrorName(err));
                RemovePadLocked();
                return;
            }
            appliedX = x;
            appliedY = y;
        }

        // Applies the desired stick state. Must be called with PadLock held.
        private static void ApplyLocked()
        {
            short dx = 0;
            short dy = 0;
            bool any = false;

            if (NudgeActiveLocked())
            {
                dy = DownValue();
                any = true;
            }
            else if (enabled && WowFocused())
            {
                any = ComputeVector(out dx, out dy);
            }

            if (any) neutralPending = false;

            if (onDemand)
            {
                if (any)
                {
                    if (!EnsurePadLocked()) return;
                    removeDueMs = -1;
                }
                else if (targetAttached)
                {
                    SendStickLocked(0, 0);
                    if (removeDueMs < 0) removeDueMs = Environment.TickCount + 800;
                    return;
                }
                else
                {
                    return;
                }
            }
            else
            {
                if (!EnsurePadLocked()) return;
            }

            SendStickLocked(dx, dy);
        }

        // ------------------------------------------------------------- keyboard
        private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            bool hitKey = false;
            if (nCode >= 0)
            {
                uint vk = (uint)Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                bool down = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool up = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

                if (down || up)
                {
                    bool isAlt = (vk == 0x12 || vk == 0xA4 || vk == 0xA5);
                    if (isAlt)
                    {
                        // A short Alt tap (no other key pressed in between) is the
                        // chat pause: translation off, keys reach the game again.
                        if (altToggle)
                        {
                            if (down)
                            {
                                if (!altDown)
                                {
                                    altDown = true;
                                    altDownMs = Environment.TickCount;
                                    altOtherKey = false;
                                }
                            }
                            else if (altDown)
                            {
                                altDown = false;
                                int heldFor = unchecked(Environment.TickCount - altDownMs);
                                if (!altOtherKey && heldFor < 600) ToggleTranslation();
                            }
                        }
                    }
                    else
                    {
                        if (down && altDown) altOtherKey = true;

                        KeyMove move = FindMove(vk);
                        if (move != null && !CtrlOrAltDown())
                        {
                            hitKey = true;
                            lock (PadLock)
                            {
                                if (down && !move.Held)
                                {
                                    move.Held = true;
                                    ApplyLocked();
                                    Log(move.Name + " down -> stick (" + appliedX + "," + appliedY + ").");
                                }
                                else if (up && move.Held)
                                {
                                    move.Held = false;
                                    ApplyLocked();
                                    Log(move.Name + " released -> stick (" + appliedX + "," + appliedY + ").");
                                }
                            }
                        }
                    }
                }
            }
            if (hitKey && swallow && enabled && !CtrlOrAltDown() && WowFocused())
            {
                // Keep the game from also handling the keyboard key: it would
                // fight the virtual stick (e.g. backpedal vs turn-and-run).
                return (IntPtr)1;
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private static void ToggleTranslation()
        {
            lock (PadLock)
            {
                enabled = !enabled;
                Log(enabled ? "Translation ON." : "Translation OFF (chat mode).");
                ApplyLocked();
            }
        }

        // Re-reads the focused window (a key may have been released elsewhere,
        // or the game may have lost / got focus while a key is held).
        private static void OnTick(object state)
        {
            lock (PadLock)
            {
                if (altToggle) RecoverAltStateLocked();
                if (onDemand && removeDueMs >= 0 && targetAttached && Environment.TickCount >= removeDueMs)
                {
                    RemovePadLocked();
                    return;
                }
                if (!targetAttached)
                {
                    if (onDemand) ApplyLocked();
                    return;
                }

                short dx = 0;
                short dy = 0;
                bool any = false;
                if (NudgeActiveLocked())
                {
                    dy = DownValue();
                    any = true;
                }
                else if (enabled && WowFocused())
                {
                    any = ComputeVector(out dx, out dy);
                }

                if (!any && neutralPending && Environment.TickCount >= neutralDueMs)
                {
                    // A freshly plugged ViGEm device drops reports for a moment
                    // until XInput starts polling it: keep sending a neutral one.
                    appliedX = 1;
                    appliedY = 1;
                    SendStickLocked(0, 0);
                    neutralTries = neutralTries + 1;
                    neutralDueMs = Environment.TickCount + 400;
                    if (neutralTries >= 8) neutralPending = false;
                }

                SendStickLocked(dx, dy);
                if (debug && (any || HeldAny()))
                {
                    Log("tick: any=" + any + " fg=" + ForegroundProcessName()
                        + " applied=(" + appliedX + "," + appliedY + ") pad=" + PadReadback());
                }
                if (onDemand && !any && removeDueMs < 0) removeDueMs = Environment.TickCount + 800;
            }
        }

        // ------------------------------------------------------------------ main
        private static void PrintHelp()
        {
            Console.WriteLine("WoW Stick Bridge v" + Version + " - virtual gamepad stick from keyboard keys.");
            Console.WriteLine();
            Console.WriteLine("Usage: WoWStickBridge.exe [options]");
            Console.WriteLine();
            Console.WriteLine("  --key <key>        key for the backward stick push (default S)");
            Console.WriteLine("  --only-s           map S only (no W, no A/D)");
            Console.WriteLine("  --no-forward       drop W;  --no-strafe  drop A/D");
            Console.WriteLine("  --x-mode <mode>    A/D rule: normal|always|backward|forward (default backward)");
            Console.WriteLine("  --forward          map W (default)");
            Console.WriteLine("  --strafe           map A / D (default)");
            Console.WriteLine("  --wasd             map W + A + S + D (default)");
            Console.WriteLine("  --stick-y <value>  stick magnitude (default 32767)");
            Console.WriteLine("  --invert           flip the vertical direction");
            Console.WriteLine("  --no-alt-toggle    do not toggle translation with a short Alt tap");
            Console.WriteLine("  --no-focus         act even when WoW is not the foreground window");
            Console.WriteLine("  --process <list>   game process names, comma separated (default Wow)");
            Console.WriteLine("  --ondemand         create the virtual pad only while a key is held");
            Console.WriteLine("  --debug            log each tick: focus, desired/applied stick, XInput");
            Console.WriteLine("  --no-swallow       let the mapped keys reach the game too (default: hidden)");
            Console.WriteLine("  --help             this text");
            Console.WriteLine();
            Console.WriteLine("Hotkeys: Ctrl+Alt+Q - exit, Ctrl+Alt+S - enable/disable translation,");
            Console.WriteLine("         Ctrl+Alt+D - force the stick down for 3 s (diagnostic).");
        }

        private static bool ParseKey(string text, out int vk)
        {
            vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.Length == 1)
            {
                char c = char.ToUpperInvariant(text[0]);
                vk = (int)c;
                return true;
            }
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int v;
                if (int.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v))
                {
                    vk = v;
                    return true;
                }
                return false;
            }
            int d;
            if (int.TryParse(text, out d))
            {
                vk = d;
                return true;
            }
            return false;
        }

        private static int Main(string[] args)
        {
            Console.Title = "WoW Stick Bridge";
            try { File.WriteAllText(LogPath, ""); } catch { }
            Console.WriteLine("WoW Stick Bridge v" + Version + " - virtual gamepad stick from keyboard keys.");
            Console.WriteLine("Log file: " + LogPath);
            Console.WriteLine();

            bool mapForward = true;   // W is mapped by default
            bool mapStrafe = true;    // A / D are mapped by default too

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--help" || a == "-h" || a == "/?")
                {
                    PrintHelp();
                    return 0;
                }
                else if (a == "--no-focus")
                {
                    onlyInWow = false;
                }
                else if (a == "--no-alt-toggle")
                {
                    altToggle = false;
                }
                else if (a == "--ondemand")
                {
                    onDemand = true;
                }
                else if (a == "--invert")
                {
                    invertY = true;
                }
                else if (a == "--debug")
                {
                    debug = true;
                }
                else if (a == "--swallow")
                {
                    swallow = true;
                }
                else if (a == "--no-swallow")
                {
                    swallow = false;
                }
                else if (a == "--only-s")
                {
                    mapForward = false;
                    mapStrafe = false;
                }
                else if (a == "--no-forward")
                {
                    mapForward = false;
                }
                else if (a == "--no-strafe")
                {
                    mapStrafe = false;
                }
                else if ((a == "--flip-x" || a == "--x-mode") && i + 1 < args.Length)
                {
                    string mode = args[++i].Trim().ToLowerInvariant();
                    if (mode == "never" || mode == "none" || mode == "normal" || mode == "0") xMode = 0;
                    else if (mode == "both" || mode == "always" || mode == "1") xMode = 1;
                    else if (mode == "backward" || mode == "s" || mode == "2") xMode = 2;
                    else if (mode == "forward" || mode == "w" || mode == "3") xMode = 3;
                    else
                    {
                        Console.WriteLine("ERROR: bad --flip-x value: " + mode);
                        return 2;
                    }
                }
                else if (a == "--forward")
                {
                    mapForward = true;
                }
                else if (a == "--strafe")
                {
                    mapStrafe = true;
                }
                else if (a == "--wasd")
                {
                    mapForward = true;
                    mapStrafe = true;
                }
                else if (a == "--key" && i + 1 < args.Length)
                {
                    int vk;
                    if (!ParseKey(args[++i], out vk))
                    {
                        Console.WriteLine("ERROR: bad --key value: " + args[i]);
                        return 2;
                    }
                    keyVk = vk;
                }
                else if (a == "--stick-y" && i + 1 < args.Length)
                {
                    short y;
                    if (!short.TryParse(args[++i], out y))
                    {
                        Console.WriteLine("ERROR: bad --stick-y value: " + args[i]);
                        return 2;
                    }
                    maxStick = Math.Abs((int)y);
                    invertY = y > 0;
                }
                else if (a == "--process" && i + 1 < args.Length)
                {
                    string[] parts = args[++i].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                    {
                        Console.WriteLine("ERROR: empty --process list.");
                        return 2;
                    }
                    for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim();
                    wowProcesses = parts;
                }
                else
                {
                    Console.WriteLine("Unknown option: " + a);
                    Console.WriteLine();
                    PrintHelp();
                    return 2;
                }
            }

            if (maxStick < 1) maxStick = 1;
            if (maxStick > 32767) maxStick = 32767;

            BuildMoves(mapForward, mapStrafe);

            Console.WriteLine("Only in WoW: " + (onlyInWow ? "yes (" + string.Join(", ", wowProcesses) + ")" : "no") + "; on-demand pad: " + (onDemand ? "yes" : "no") + ".");
            Console.WriteLine("Debug: " + (debug ? "on" : "off") + "; hide mapped keys from game: " + (swallow ? "on" : "off") + ".");
            Console.WriteLine("Chat: tap Alt (short press, alone) - translation on/off; Ctrl+Alt+S does the same.");
            Console.WriteLine("Hotkeys: Ctrl+Alt+Q - exit, Ctrl+Alt+S - enable/disable, Ctrl+Alt+D - 3 s stick test,");
            Console.WriteLine("         Ctrl+Alt+X - cycle A/D rule (normal / flip always / flip when S / flip when W).");
            Console.WriteLine("X mode: " + XModeName(xMode) + ".");
            for (int i = 0; i < moves.Count; i++)
            {
                Console.WriteLine("  " + moves[i].Name + " -> stick (" + moves[i].X + "," + moves[i].Y + ")");
            }
            Console.WriteLine();

            if (!IsSuccess(ConnectViGEm())) return 1;

            hookProc = new LowLevelKeyboardProc(HookProc);
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                Log("ERROR: SetWindowsHookEx failed (last error " + Marshal.GetLastWin32Error() + ").");
                DisconnectViGEm();
                return 1;
            }

            RegisterHotKey(IntPtr.Zero, HOTKEY_QUIT, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x51); // Q
            RegisterHotKey(IntPtr.Zero, HOTKEY_TOGGLE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x53); // S
            RegisterHotKey(IntPtr.Zero, HOTKEY_NUDGE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x44); // D
            RegisterHotKey(IntPtr.Zero, HOTKEY_XMODE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x58); // X

            if (!onDemand)
            {
                lock (PadLock)
                {
                    if (!EnsurePadLocked())
                    {
                        Cleanup();
                        return 1;
                    }
                }
            }

            ticker = new Timer(OnTick, null, 250, 250);
            Log("Ready (v" + Version + "). Mapped keys: " + moves.Count + "; x mode: " + XModeName(xMode) + ".");

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY)
                {
                    int id = msg.wParam.ToInt32();
                    if (id == HOTKEY_QUIT) break;
                    if (id == HOTKEY_TOGGLE)
                    {
                        ToggleTranslation();
                    }
                    if (id == HOTKEY_NUDGE)
                    {
                        lock (PadLock)
                        {
                            nudgeUntil = Environment.TickCount + 3000;
                            Log("NUDGE start: left stick down for 3 s (diagnostic).");
                            ApplyLocked();
                        }
                    }
                    if (id == HOTKEY_XMODE)
                    {
                        lock (PadLock)
                        {
                            xMode = (xMode + 1) % 4;
                            Log("X mode -> " + XModeName(xMode) + ".");
                            ApplyLocked();
                        }
                    }
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Log("Exiting.");
            Cleanup();
            return 0;
        }

        private static int ConnectViGEm()
        {
            client = vigem_alloc();
            if (client == IntPtr.Zero)
            {
                Log("ERROR: vigem_alloc() returned NULL (ViGEmClient.dll missing? put it next to the exe).");
                return unchecked((int)0xE0000001);
            }
            int err = vigem_connect(client);
            if (!IsSuccess(err))
            {
                Log("ERROR: vigem_connect() -> " + ErrorName(err));
                vigem_free(client);
                client = IntPtr.Zero;
                return err;
            }
            Log("Connected to ViGEmBus.");
            return VIGEM_ERROR_NONE;
        }

        private static void DisconnectViGEm()
        {
            if (client != IntPtr.Zero)
            {
                vigem_disconnect(client);
                vigem_free(client);
                client = IntPtr.Zero;
            }
        }

        private static void Cleanup()
        {
            try
            {
                lock (PadLock)
                {
                    if (targetAttached) RemovePadLocked();
                }
                if (ticker != null)
                {
                    ticker.Dispose();
                    ticker = null;
                }
                UnregisterHotKey(IntPtr.Zero, HOTKEY_QUIT);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_TOGGLE);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_NUDGE);
                UnregisterHotKey(IntPtr.Zero, HOTKEY_XMODE);
                if (hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                }
                DisconnectViGEm();
            }
            catch (Exception ex)
            {
                Log("Cleanup error: " + ex.Message);
            }
        }
    }
}
