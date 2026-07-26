using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

public sealed class DroidTrakrOverlay : Window {
  const string Api = "https://droidtrakr.com/api";
  const int HotkeyMode = 0xD801, HotkeyLock = 0xD802, HotkeySearch = 0xD803, WM_HOTKEY = 0x0312;
  const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
  readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
  readonly string RememberPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DroidTrakr","remembered-session.dat");
  string PreferredGroupPath(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DroidTrakr","selected-group-"+Key(S(User,"id"))+".txt");}
  string LoadPreferredGroup(){try{return User==null?"":File.ReadAllText(PreferredGroupPath()).Trim();}catch{return "";}}
  void SavePreferredGroup(string gid){try{var path=PreferredGroupPath();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,gid??"");}catch{}}
  readonly CookieContainer Cookies = new CookieContainer();
  readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2000000 };
  readonly DispatcherTimer Tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
  DispatcherTimer SaveDebounce;
  Dictionary<string, object> PendingSaveMember;
  readonly Dictionary<string,object> PendingSaveChanges=new Dictionary<string,object>();
  bool PendingSaveFullSnapshot;
  bool RebirthSavePending,RebirthSaveInFlight;
  int RebirthSaveGeneration;
  static Dictionary<string, BitmapImage> ImageCache = new Dictionary<string, BitmapImage>();
  readonly Dictionary<string,FrameworkElement> VisibleNeedTiles=new Dictionary<string,FrameworkElement>();
  readonly Random PoofRandom=new Random();
  Dictionary<string, object> User, LastData, SelectedGroup;
  List<Dictionary<string, object>> AvailableGroups = new List<Dictionary<string, object>>();
  List<object> Catalog = new List<object>();
  DateTime LastRefresh = DateTime.MinValue, LastHeartbeat = DateTime.MinValue, LastHeartbeatAttempt = DateTime.MinValue, LastLimitedDealFetch = DateTime.MinValue, LastInviteFetch = DateTime.MinValue;
  Dictionary<string,object> LimitedDeal;
  List<Dictionary<string,object>> UndoHistory=new List<Dictionary<string,object>>();
  List<string> UndoMemberIds=new List<string>(),UndoDescriptions=new List<string>();
  const int MaxUndoHistory=25;
  string LastLimitedDealSignature="";
  bool Locked, Busy, ShowCompleted, SearchMode, VendorMode, RefreshQueued;
  string VendorSelectedDroid="",VendorSelectedTier="Default";
  List<Dictionary<string,object>> PendingInvites=new List<Dictionary<string,object>>();
  HashSet<string> SeenInviteIds=new HashSet<string>();
  TimeSpan ServerClockOffset=TimeSpan.Zero;
  volatile bool EventStreamStarted=false,EventStreamConnected=false;
  Dictionary<string,object> LimitedDealNotice;
  DateTime LimitedDealNoticeUntil=DateTime.MinValue;
  int Page;
  string Mode = "mine";
  Grid RootGrid;
  TextBlock Status, Timers, ModeTitle, MythicTimerText, GalacticTimerText, BeskarTimerText;
  TextBox SearchBox;
  Button SearchClose;
  Viewbox OverlayViewbox;
  double OverlayScale=1.0,LogicalOverlayHeight=450;
  Border MythicFill, GalacticFill, BeskarFill;
  Window TimerHud;
  Forms.NotifyIcon TrayIcon;
  StackPanel HudContent, Rail;
  IntPtr Hwnd;
  IntPtr CachedFortniteWindow=IntPtr.Zero;
  double OverlayTopRatio = Double.NaN;
  DateTime LastAnchorAttempt=DateTime.MinValue;
  int FortniteMissingTicks=0;
  static void Log(string message) { try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay-launch.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch { } }

  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct NATIVEPOINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] struct WINDOWPLACEMENT { public int length, flags, showCmd; public PointMin ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }
  [StructLayout(LayoutKind.Sequential)] struct PointMin { public int X, Y; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref NATIVEPOINT p);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT placement);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int n);
  [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int n, int v);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

  public DroidTrakrOverlay() {
    Log("WPF constructor entered");
    Title = "DroidTrakr Fortnite Overlay";
    WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
    AllowsTransparency = true; Background = Brushes.Transparent;
    ShowInTaskbar = false; Topmost = true; Width = 820; Height = 480;
    FontFamily = new FontFamily("Segoe UI");
    try { Catalog = L(Json.DeserializeObject(File.ReadAllText(Path.Combine(Root, "rebirth-cycles.json")))); } catch { }
    Loaded += delegate { Log("WPF window loaded"); SetupTrayIcon(); CenterLogin(); ShowLogin(); Tick.Start();Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{if(CheckFortniteDisplayMode())TryRestoreRememberedSession();})); Log("login state rendered"); };
    SourceInitialized += OnSourceInitialized;
    Closed += delegate { if(TrayIcon!=null){TrayIcon.Visible=false;TrayIcon.Dispose();TrayIcon=null;} if (TimerHud != null) TimerHud.Close(); if (Hwnd != IntPtr.Zero) { UnregisterHotKey(Hwnd, HotkeyMode); UnregisterHotKey(Hwnd, HotkeyLock); UnregisterHotKey(Hwnd, HotkeySearch); } };
    Tick.Tick += delegate { UpdateTimers();if(User!=null&&!EventStreamStarted)StartEventStream();if(LimitedDealNotice!=null&&DateTime.UtcNow>=LimitedDealNoticeUntil){LimitedDealNotice=null;if(User!=null)Render();}if(User!=null&&(DateTime.UtcNow-LastAnchorAttempt).TotalSeconds>=2){LastAnchorAttempt=DateTime.UtcNow;AnchorToFortnite();}if(User!=null&&(DateTime.UtcNow-LastHeartbeatAttempt).TotalSeconds>=30)SendOverlayHeartbeat();if(User!=null&&(DateTime.UtcNow-LastLimitedDealFetch).TotalSeconds>=30)FetchLimitedDeal();if(User!=null&&!EventStreamConnected&&(DateTime.UtcNow-LastInviteFetch).TotalSeconds>=20)FetchInvitations(); if (User != null && !Busy && (DateTime.UtcNow - LastRefresh).TotalSeconds >= (Mode=="overview"?30:45)) RefreshData(); };
  }

  bool CheckFortniteDisplayMode(){
    try{
      var config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FortniteGame","Saved","Config","WindowsClient","GameUserSettings.ini");if(!File.Exists(config))return true;var text=File.ReadAllText(config);var match=Regex.Match(text,@"(?m)^FullscreenMode=(\d+)\s*$");if(!match.Success||match.Groups[1].Value!="0")return true;
      var running=Process.GetProcessesByName("FortniteClient-Win64-Shipping").Length>0;
      if(running){MessageBox.Show(this,"Fortnite is in Fullscreen mode.\n\nSet Settings > Video > Window Mode to WINDOWED FULLSCREEN, then reopen DroidTrakr.","Windowed Fullscreen Required",MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
      var answer=MessageBox.Show(this,"Fortnite is configured for Exclusive Fullscreen, which hides DroidTrakr whenever Fortnite is focused.\n\nWould you like DroidTrakr to safely change Fortnite to WINDOWED FULLSCREEN now? A backup of GameUserSettings.ini will be created first.","DroidTrakr: Fix Fortnite Display Mode",MessageBoxButton.YesNo,MessageBoxImage.Warning);
      if(answer!=MessageBoxResult.Yes)return false;var backup=config+".droidtrakr-backup";File.Copy(config,backup,true);var updated=Regex.Replace(text,@"(?m)^FullscreenMode=0\s*$","FullscreenMode=1");updated=Regex.Replace(updated,@"(?m)^LastConfirmedFullscreenMode=0\s*$","LastConfirmedFullscreenMode=1");updated=Regex.Replace(updated,@"(?m)^PreferredFullscreenMode=0\s*$","PreferredFullscreenMode=1");File.WriteAllText(config,updated,new UTF8Encoding(false));MessageBox.Show(this,"Fortnite has been changed to Windowed Fullscreen.\n\nA backup was created at:\n"+backup+"\n\nStart Fortnite normally. DroidTrakr should now remain visible while the game is focused.","DroidTrakr: Display Mode Updated",MessageBoxButton.OK,MessageBoxImage.Information);return true;
    }catch(Exception ex){Log("Display mode check failed: "+ex.Message);return true;}
  }

  void SetupTrayIcon(){try{TrayIcon=new Forms.NotifyIcon();var exe=Process.GetCurrentProcess().MainModule.FileName;TrayIcon.Icon=Drawing.Icon.ExtractAssociatedIcon(exe);TrayIcon.Text="DroidTrakr Fortnite Overlay";var menu=new Forms.ContextMenuStrip();var open=menu.Items.Add("Open DroidTrakr");open.Click+=delegate{Dispatcher.Invoke(delegate{if(User==null){Show();Activate();}else AnchorToFortnite();});};var lockItem=menu.Items.Add("Toggle click-through (F9)");lockItem.Click+=delegate{Dispatcher.Invoke(delegate{if(User!=null)ToggleLock();});};menu.Items.Add(new Forms.ToolStripSeparator());var exit=menu.Items.Add("Exit DroidTrakr");exit.Click+=delegate{Dispatcher.Invoke(delegate{Close();});};TrayIcon.ContextMenuStrip=menu;TrayIcon.DoubleClick+=delegate{Dispatcher.Invoke(delegate{if(User==null){Show();Activate();}else AnchorToFortnite();});};TrayIcon.Visible=true;}catch(Exception ex){Log("Tray icon unavailable: "+ex.Message);}}

  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] struct PROCESSENTRY32 { public uint dwSize,cntUsage,th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID,cntThreads,th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string szExeFile; }
  const uint TH32CS_SNAPPROCESS=2;
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint processId);
  [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern bool Process32First(IntPtr snapshot,ref PROCESSENTRY32 entry);
  [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern bool Process32Next(IntPtr snapshot,ref PROCESSENTRY32 entry);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

  static bool LaunchedByDroidTrakrLauncher(){IntPtr snap=IntPtr.Zero;try{snap=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);if(snap==new IntPtr(-1))return false;var entry=new PROCESSENTRY32{dwSize=(uint)Marshal.SizeOf(typeof(PROCESSENTRY32))};var own=(uint)Process.GetCurrentProcess().Id;if(Process32First(snap,ref entry)){do{if(entry.th32ProcessID==own){var parent=Process.GetProcessById((int)entry.th32ParentProcessID);return String.Equals(parent.ProcessName,"DroidTrakr Launcher",StringComparison.OrdinalIgnoreCase);}}while(Process32Next(snap,ref entry));}}catch{}finally{if(snap!=IntPtr.Zero&&snap!=new IntPtr(-1))CloseHandle(snap);}return false;}

  static bool ValidateLauncherGate(){try{var args=Environment.GetCommandLineArgs();var index=Array.IndexOf(args,"--launcher-token");if(index<0||index+1>=args.Length)return LaunchedByDroidTrakrLauncher();var supplied=args[index+1];var gate=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".launcher-session");if(!File.Exists(gate))return false;var raw=File.ReadAllText(gate).Split('|');try{File.Delete(gate);}catch{}if(raw.Length!=2||raw[0]!=supplied)return false;long expires=0;if(!Int64.TryParse(raw[1],out expires))return false;var now=DateTime.UtcNow.Ticks;return expires>=now&&expires<=DateTime.UtcNow.AddMinutes(3).Ticks;}catch{return LaunchedByDroidTrakrLauncher();}}

  void OnSourceInitialized(object sender, EventArgs e) {
    Hwnd = new WindowInteropHelper(this).Handle;
    HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
    RegisterHotKey(Hwnd, HotkeyMode, 0, 0x77); // F8
    RegisterHotKey(Hwnd, HotkeyLock, 0, 0x78); // F9
    RegisterHotKey(Hwnd, HotkeySearch, 0, 0x79); // F10
  }
  IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
    if (msg == WM_HOTKEY) {
      if (wParam.ToInt32() == HotkeyMode && User != null) ToggleMode();
      if (wParam.ToInt32() == HotkeyLock && User != null) ToggleLock();
      if (wParam.ToInt32() == HotkeySearch && User != null) ToggleSearchMode();
      handled = true;
    }
    return IntPtr.Zero;
  }

  Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
  List<object> L(object o) {
    var result = new List<object>(); if (o == null || o is string) return result;
    var enumerable = o as IEnumerable; if (enumerable != null) foreach (var item in enumerable) result.Add(item);
    return result;
  }
  string S(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] != null ? Convert.ToString(d[k]) : ""; }
  bool B(Dictionary<string, object> d, string k) { bool v; return d != null && d.ContainsKey(k) && Boolean.TryParse(Convert.ToString(d[k]), out v) && v; }
  SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
  TextBlock Text(string value, double size, Brush color, FontWeight weight) { return new TextBlock { Text = value, FontSize = size, Foreground = color, FontWeight = weight }; }

  object Request(string path, string method, object body) {
    var req = (HttpWebRequest)WebRequest.Create(Api + path); req.Method = method; req.CookieContainer = Cookies;
    req.ContentType = "application/json"; req.Accept = "application/json"; req.Timeout = 15000; req.ReadWriteTimeout = 15000;
    if (body != null) { var bytes = Encoding.UTF8.GetBytes(Json.Serialize(body)); req.ContentLength = bytes.Length; using (var st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length); }
    try { using (var res = (HttpWebResponse)req.GetResponse()) {DateTimeOffset serverDate;if(DateTimeOffset.TryParse(res.Headers[HttpResponseHeader.Date],out serverDate))ServerClockOffset=serverDate.ToUniversalTime()-DateTimeOffset.UtcNow;using (var sr = new StreamReader(res.GetResponseStream())) return Json.DeserializeObject(sr.ReadToEnd());} }
    catch (WebException ex) {
      if (ex.Response != null) using (var sr = new StreamReader(ex.Response.GetResponseStream())) { var raw = sr.ReadToEnd(); Dictionary<string, object> parsed = null; try { parsed = D(Json.DeserializeObject(raw)); } catch { } throw new Exception(parsed != null ? S(parsed, "error") : raw); }
      throw new Exception("Could not reach DroidTrakr securely. " + ex.Status);
    }
  }
  object Get(string path) { return Request(path, "GET", null); }
  void StartEventStream(){
    if(EventStreamStarted)return;EventStreamStarted=true;
    Task.Factory.StartNew(delegate{
      while(true){
        try{
          if(User==null){EventStreamConnected=false;System.Threading.Thread.Sleep(2000);continue;}
          var req=(HttpWebRequest)WebRequest.Create(Api+"/stream/events");req.Method="GET";req.CookieContainer=Cookies;req.Accept="text/event-stream";req.Timeout=System.Threading.Timeout.Infinite;req.ReadWriteTimeout=45000;req.KeepAlive=true;
          using(var res=(HttpWebResponse)req.GetResponse())using(var reader=new StreamReader(res.GetResponseStream())){
            EventStreamConnected=true;Dispatcher.BeginInvoke(new Action(delegate{FetchInvitations();LastRefresh=DateTime.MinValue;RefreshData();}));string eventType="",line;
            while((line=reader.ReadLine())!=null){
              if(line.StartsWith("event:"))eventType=line.Substring(6).Trim();
              else if(line.StartsWith("data:")){var type=eventType;eventType="";Dictionary<string,object> eventData=null;try{eventData=D(Json.DeserializeObject(line.Substring(5).Trim()));}catch{}var source=S(eventData,"source");var eventUser=S(eventData,"userId");Dispatcher.BeginInvoke(new Action(delegate{if(type=="group.invite.created"||type=="group.invite.responded")FetchInvitations();else if(type=="group.membership.updated"||(type=="rebirth.updated"&&!(source=="overlay"&&eventUser==S(User,"id")))){LastRefresh=DateTime.MinValue;RefreshData();}}));}
            }
          }
        }catch(Exception ex){Log("Event stream reconnect: "+ex.Message);}finally{EventStreamConnected=false;}
        System.Threading.Thread.Sleep(3000);
      }
    });
  }

  void CenterLogin() { var a = SystemParameters.WorkArea; Left = a.Left + (a.Width - Width) / 2; Top = a.Top + (a.Height - Height) / 2; }
  void ClearRememberedSession(){try{if(File.Exists(RememberPath))File.Delete(RememberPath);}catch{}}
  void SaveRememberedSession(){try{if(User==null)return;var cookie=Cookies.GetCookies(new Uri(Api))["droidtrakr_session"];if(cookie==null||String.IsNullOrWhiteSpace(cookie.Value))return;var payload=Json.Serialize(new Dictionary<string,object>{{"token",cookie.Value},{"user",User}});var encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(payload),null,DataProtectionScope.CurrentUser);Directory.CreateDirectory(Path.GetDirectoryName(RememberPath));File.WriteAllBytes(RememberPath,encrypted);}catch(Exception ex){Log("Could not remember session: "+ex.Message);}}
  void TryRestoreRememberedSession(){if(!File.Exists(RememberPath))return;Task.Factory.StartNew(delegate{try{var encrypted=File.ReadAllBytes(RememberPath);var raw=Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser));var saved=D(Json.DeserializeObject(raw));var user=saved!=null&&saved.ContainsKey("user")?D(saved["user"]):null;var token=S(saved,"token");if(user==null||String.IsNullOrWhiteSpace(token))throw new Exception("empty remembered session");Cookies.Add(new Uri(Api),new Cookie("droidtrakr_session",token,"/","droidtrakr.com"){Secure=true,HttpOnly=true});var uid=S(user,"id");var test=D(Get("/users/"+Uri.EscapeDataString(uid)+"/groups?t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));if(test==null||!test.ContainsKey("groups"))throw new Exception("session expired");return user;}catch{ClearRememberedSession();return null;}}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted||t.Result==null)return;User=t.Result;ShowOverlayShell();RefreshData();}));}

  void ShowLogin() {
    Topmost = false;
    Locked = false; if (TimerHud != null) TimerHud.Hide(); SetClickThrough(false); Width = 820; Height = 480; Background = Brush("#07101E"); CenterLogin();
    var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
    var art = new Grid { Background = Brush("#0D1B31") }; art.RowDefinitions.Add(new RowDefinition());
    var glow = new Border { Width = 260, Height = 260, CornerRadius = new CornerRadius(130), Background = Brush("#182D63A8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new BlurEffect { Radius = 55 } }; art.Children.Add(glow);
    var left = new StackPanel { Margin = new Thickness(30, 28, 25, 25) };
    left.Children.Add(Text("DROIDTRAKR", 26, Brushes.White, FontWeights.Bold));
    var sub = Text("FORTNITE OVERLAY CLIENT", 10, Brush("#98AAC7"), FontWeights.SemiBold); sub.Margin = new Thickness(1, 2, 0, 0); left.Children.Add(sub);
    var rule = new Border { Width = 78, Height = 3, Background = Brush("#8B5CF6"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 23, 0, 18) }; left.Children.Add(rule);
    left.Children.Add(Text("LIVE REBIRTH INTELLIGENCE", 12, Brush("#D6E2F3"), FontWeights.Bold));
    var desc = Text("Your required droids. Your crew.\nSynced securely from DroidTrakr.", 11, Brush("#90A3C0"), FontWeights.Normal); desc.Margin = new Thickness(0, 7, 0, 16); left.Children.Add(desc);
    var arts = new Grid { Height = 180 }; arts.ColumnDefinitions.Add(new ColumnDefinition()); arts.ColumnDefinitions.Add(new ColumnDefinition()); arts.ColumnDefinitions.Add(new ColumnDefinition());
    AddLoginArt(arts, "R2", 0, 84); AddLoginArt(arts, "PROTOROLLER", 1, 118); AddLoginArt(arts, "BB9", 2, 82); left.Children.Add(arts);
    var build = Text("CLIENT BUILD  2.0 WPF", 9, Brush("#657794"), FontWeights.SemiBold); build.Margin = new Thickness(0, 13, 0, 0); left.Children.Add(build); art.Children.Add(left); shell.Children.Add(art);

    var right = new Grid { Background = Brush("#091324"), Margin = new Thickness(0) }; Grid.SetColumn(right, 1);
    var form = new StackPanel { Width = 388, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    form.Children.Add(Text("CONNECT DROIDTRAKR", 22, Brushes.White, FontWeights.Bold));
    var hint = Text("Authorize with your existing DroidTrakr account.", 11, Brush("#8FA2C0"), FontWeights.Normal); hint.Margin = new Thickness(0, 5, 0, 25); form.Children.Add(hint);
    form.Children.Add(FieldLabel("ACCOUNT NAME")); var username = LoginTextBox(); form.Children.Add(username);
    form.Children.Add(FieldLabel("PASSWORD")); var password = new PasswordBox { Height = 38, Background = Brush("#101C30"), Foreground = Brushes.White, BorderBrush = Brush("#2A3A55"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 6), FontSize = 13, Margin = new Thickness(0, 6, 0, 12) }; form.Children.Add(password);
    var remember = new CheckBox { Content="Remember this account on this Windows user",Foreground=Brush("#B9C7DB"),FontSize=10,Margin=new Thickness(0,-3,0,10),IsChecked=true };
    form.Children.Add(remember);
    var state = Text("READY TO CONNECT", 10, Brush("#72E3B8"), FontWeights.Bold); state.Margin = new Thickness(0, 2, 0, 10); form.Children.Add(state);
    var go = new Button { Content = "CONNECT OVERLAY", Height = 43, Background = Brush("#6D45C7"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand }; form.Children.Add(go);
    var create = new Button { Content = "CREATE WITH USERNAME", Height = 32, Margin=new Thickness(0,7,0,0), Background = Brush("#13213A"), Foreground = Brush("#C9D5E8"), BorderBrush=Brush("#344764"), BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand }; form.Children.Add(create);
    var privacy = Text("PASSWORD NEVER STORED  |  SESSION PROTECTED BY WINDOWS", 9, Brush("#586B88"), FontWeights.SemiBold); privacy.Margin = new Thickness(0, 14, 0, 0); form.Children.Add(privacy);
    right.Children.Add(form);
    var close = new Button { Content = "X", Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 14, 0), Background = Brush("#172338"), Foreground = Brush("#AAB7CC"), BorderThickness = new Thickness(0) }; close.Click += delegate { Close(); }; right.Children.Add(close);
    shell.Children.Add(right); Content = shell; username.Focus();
    go.Click += delegate {
      var name = username.Text.Trim(); var pass = password.Password;
      if (name.Length == 0 || pass.Length == 0) { state.Text = "ENTER ACCOUNT NAME AND PASSWORD"; state.Foreground = Brush("#F1B66D"); return; }
      go.IsEnabled = false; go.Content = "CONNECTING..."; state.Text = "AUTHENTICATING WITH DROIDTRAKR"; state.Foreground = Brush("#A78BFA"); Busy = true;
      Task.Factory.StartNew(delegate { return D(Request("/users", "POST", new Dictionary<string, object> { { "mode", "login" }, { "name", name }, { "password", pass } })); }).ContinueWith(t => Dispatcher.Invoke(delegate {
        password.Clear(); Busy = false;
        if (t.IsFaulted) { state.Text = (t.Exception.InnerException != null ? t.Exception.InnerException.Message : t.Exception.Message).ToUpperInvariant(); state.Foreground = Brush("#EF7478"); go.IsEnabled = true; go.Content = "CONNECT OVERLAY"; return; }
        if (t.Result == null || !t.Result.ContainsKey("user")) { state.Text = "INVALID LOGIN RESPONSE"; go.IsEnabled = true; go.Content = "CONNECT OVERLAY"; return; }
        User = D(t.Result["user"]);if(remember.IsChecked==true)SaveRememberedSession();else ClearRememberedSession(); ShowOverlayShell(); RefreshData();
      }));
    };
    create.Click += delegate {
      var name=username.Text.Trim();var pass=password.Password;
      if(name.Length==0||pass.Length==0){state.Text="ENTER A NAME AND PASSWORD TO CREATE AN ACCOUNT";state.Foreground=Brush("#F1B66D");return;}
      if(pass.Length<4){state.Text="PASSWORD MUST BE AT LEAST 4 CHARACTERS";state.Foreground=Brush("#F1B66D");return;}
      go.IsEnabled=false;create.IsEnabled=false;create.Content="CREATING ACCOUNT...";state.Text="CREATING SECURE DROIDTRAKR ACCOUNT";state.Foreground=Brush("#A78BFA");Busy=true;
      Task.Factory.StartNew(delegate{return D(Request("/users","POST",new Dictionary<string,object>{{"mode","register"},{"name",name},{"password",pass}}));}).ContinueWith(t=>Dispatcher.Invoke(delegate{
        password.Clear();Busy=false;
        if(t.IsFaulted){state.Text=(t.Exception.InnerException!=null?t.Exception.InnerException.Message:t.Exception.Message).ToUpperInvariant();state.Foreground=Brush("#EF7478");go.IsEnabled=true;create.IsEnabled=true;create.Content="CREATE NEW ACCOUNT";return;}
        if(t.Result==null||!t.Result.ContainsKey("user")){state.Text="INVALID ACCOUNT RESPONSE";go.IsEnabled=true;create.IsEnabled=true;create.Content="CREATE NEW ACCOUNT";return;}
        User=D(t.Result["user"]);if(remember.IsChecked==true)SaveRememberedSession();else ClearRememberedSession();ShowOverlayShell();RefreshData();
      }));
    };
  }
  TextBlock FieldLabel(string s) { var t = Text(s, 9, Brush("#8294B1"), FontWeights.Bold); t.Margin = new Thickness(0, 0, 0, 0); return t; }
  TextBox LoginTextBox() { return new TextBox { Height = 38, Background = Brush("#101C30"), Foreground = Brushes.White, BorderBrush = Brush("#2A3A55"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 6), FontSize = 13, Margin = new Thickness(0, 6, 0, 15) }; }
  void AddLoginArt(Grid grid, string key, int col, double size) { var image = DroidImage(key, size); if (image != null) { image.VerticalAlignment = VerticalAlignment.Bottom; image.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(image, col); grid.Children.Add(image); } }

  void ShowOverlayShell() {
    Topmost = true;
    Background = Brushes.Transparent; LogicalOverlayHeight=Mode=="mine"?650:450;Width=560;Height=LogicalOverlayHeight;
    RootGrid = new Grid{Width=560,Height=LogicalOverlayHeight}; RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); RootGrid.RowDefinitions.Add(new RowDefinition());
    var toolbar=new StackPanel{Orientation=Orientation.Vertical,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,1,9,0)};var brand=Text("DroidTrakr.com",9,Brush("#9BAFCC"),FontWeights.SemiBold);brand.HorizontalAlignment=HorizontalAlignment.Right;brand.Margin=new Thickness(0,0,2,1);toolbar.Children.Add(brand);Rail = new StackPanel { Height = 20, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    Rail.Children.Add(RailButton("M", delegate { ToggleMode(); }, "F8 mode")); Rail.Children.Add(RailButton("D", delegate { ShowCompleted=!ShowCompleted; Page=0; Render(); }, "Show/hide completed droids")); Rail.Children.Add(RailButton("S", delegate { ToggleSearchMode(); }, "Search droids in overlay")); Rail.Children.Add(RailButton("H", delegate { ShowGeneralChat(); }, "General chat")); var vendorButton=RailButton("DEAL", delegate { ToggleVendorMode(); }, "Secret Vendor limited deal" );vendorButton.Width=38;vendorButton.Background=Brush("#B9166534");vendorButton.Foreground=Brush("#DCFCE7");vendorButton.BorderBrush=Brush("#4ADE80");Rail.Children.Add(vendorButton); Rail.Children.Add(RailButton("C", delegate { ShowCycleManager(); }, "Change Rebirth cycle")); Rail.Children.Add(RailButton("G", delegate { ShowGroupManager(); }, "Groups")); Rail.Children.Add(RailButton("^", delegate { ChangePage(-1); }, "Previous page")); Rail.Children.Add(RailButton("v", delegate { ChangePage(1); }, "Next page")); var undoButton=RailButton("UNDO", delegate { UndoLastChange(); }, "Undo last droid or cycle reset change");undoButton.Width=34;Rail.Children.Add(undoButton); Rail.Children.Add(RailButton("L", delegate { ToggleLock(); }, "F9 lock")); var exitButton=RailButton("X",delegate{Close();},"Close");exitButton.Background=Brush("#B97F1D1D");exitButton.BorderBrush=Brush("#F87171");exitButton.Foreground=Brush("#FEE2E2");Rail.Children.Add(exitButton);toolbar.Children.Add(Rail);RootGrid.Children.Add(toolbar);
    var hud = new Grid { Margin = new Thickness(8, 0, 10, 8) }; hud.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); hud.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); hud.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(hud, 1);
    var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) }; heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    ModeTitle = Text("", 1, Brushes.Transparent, FontWeights.Normal); ModeTitle.Visibility = Visibility.Collapsed; heading.Children.Add(ModeTitle);
    SearchBox=LoginTextBox();SearchBox.Width=220;SearchBox.Height=26;SearchBox.Padding=new Thickness(8,4,8,3);SearchBox.FontSize=11;SearchBox.HorizontalAlignment=HorizontalAlignment.Right;SearchBox.Margin=new Thickness(0,0,30,5);SearchBox.Visibility=Visibility.Collapsed;SearchBox.ToolTip="Search by droid, tier, or Rebirth row";SearchBox.TextChanged+=delegate{if(SearchMode){Page=0;Render();}};heading.Children.Add(SearchBox);
    SearchClose=new Button{Content="X",Width=24,Height=24,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,1,2,0),Background=Brush("#A87F1D2D"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontSize=9,FontWeight=FontWeights.Bold,Cursor=Cursors.Hand,Visibility=Visibility.Collapsed,ToolTip="Close search"};SearchClose.Click+=delegate{ToggleSearchMode();};heading.Children.Add(SearchClose);
    Timers = Text("", 1, Brushes.Transparent, FontWeights.Normal); hud.Children.Add(heading);
    Status = Text("", 1, Brushes.Transparent, FontWeights.Normal); Status.Visibility = Visibility.Collapsed; Status.Margin = new Thickness(0); Grid.SetRow(Status, 1); hud.Children.Add(Status);
    HudContent = new StackPanel(); var scroll = new ScrollViewer { Content = HudContent, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.None }; Grid.SetRow(scroll, 2); hud.Children.Add(scroll); RootGrid.Children.Add(hud); OverlayViewbox=new Viewbox{Stretch=Stretch.Fill,Child=RootGrid};base.Content=OverlayViewbox; EnsureTimerHud(); UpdateTimers(); AnchorToFortnite();
  }
  Button RailButton(string text, RoutedEventHandler action, string tip) { var b = new Button { Content = text, Width = 24, Height = 19, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0), Background = Brush("#A8141D32"), Foreground = Brushes.White, BorderBrush = Brush("#456E5BFF"), BorderThickness = new Thickness(1), FontSize = 8, FontWeight = FontWeights.Bold, ToolTip = tip, Cursor = Cursors.Hand }; b.Click += action; return b; }
  DropShadowEffect Shadow() { return null; }

  void EnsureTimerHud() {
    if (TimerHud != null) { TimerHud.Show(); return; }
    TimerHud = new Window { Width = 620, Height = 72, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, IsHitTestVisible = false };
    var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    var beskar = TimerPill("BESKAR", "beskar", out BeskarTimerText, out BeskarFill); beskar.Margin = new Thickness(0,0,8,0); line.Children.Add(beskar);
    var mythic = TimerPill("MYTHIC", "mythic", out MythicTimerText, out MythicFill); mythic.Margin = new Thickness(0,0,8,0); line.Children.Add(mythic);
    var galactic = TimerPill("GALACTIC", "galactic", out GalacticTimerText, out GalacticFill); line.Children.Add(galactic);
    TimerHud.Content=new Viewbox{Stretch=Stretch.Uniform,Child=line}; TimerHud.Show();
  }
  Border TimerPill(string label, string theme, out TextBlock value, out Border fill) {
    bool mythic=theme=="mythic",galactic=theme=="galactic";
    var fillGradient=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5)};
    Brush shell;var edge=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};Brush labelBrush;
    if(mythic){
      fillGradient.GradientStops.Add(new GradientStop(BrushColor("#7A102A"),0));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#E11D48"),.55));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#9F1239"),1));
      var red=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};red.GradientStops.Add(new GradientStop(BrushColor("#21050B"),0));red.GradientStops.Add(new GradientStop(BrushColor("#0B0205"),.58));red.GradientStops.Add(new GradientStop(BrushColor("#2B0711"),1));shell=red;
      edge.GradientStops.Add(new GradientStop(BrushColor("#FDA4AF"),0));edge.GradientStops.Add(new GradientStop(BrushColor("#E11D48"),.5));edge.GradientStops.Add(new GradientStop(BrushColor("#881337"),1));labelBrush=Brush("#FDA4AF");
    }else if(galactic){
      fillGradient.GradientStops.Add(new GradientStop(BrushColor("#164E63"),0));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#2563EB"),.45));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#7C3AED"),1));
      var space=new RadialGradientBrush{GradientOrigin=new Point(.28,.3),Center=new Point(.5,.5),RadiusX=.9,RadiusY=.9};space.GradientStops.Add(new GradientStop(BrushColor("#132044"),0));space.GradientStops.Add(new GradientStop(BrushColor("#080A1C"),.62));space.GradientStops.Add(new GradientStop(BrushColor("#03040C"),1));shell=space;
      edge.GradientStops.Add(new GradientStop(BrushColor("#67E8F9"),0));edge.GradientStops.Add(new GradientStop(BrushColor("#818CF8"),.48));edge.GradientStops.Add(new GradientStop(BrushColor("#C084FC"),1));labelBrush=Brush("#C4B5FD");
    }else{
      fillGradient.GradientStops.Add(new GradientStop(BrushColor("#28313B"),0));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#CBD5E1"),.32));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#475569"),.58));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#E2E8F0"),.8));fillGradient.GradientStops.Add(new GradientStop(BrushColor("#334155"),1));
      var steel=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};steel.GradientStops.Add(new GradientStop(BrushColor("#070A0E"),0));steel.GradientStops.Add(new GradientStop(BrushColor("#19212B"),.34));steel.GradientStops.Add(new GradientStop(BrushColor("#0A0E14"),.55));steel.GradientStops.Add(new GradientStop(BrushColor("#202A35"),.78));steel.GradientStops.Add(new GradientStop(BrushColor("#06080C"),1));shell=steel;
      edge.GradientStops.Add(new GradientStop(BrushColor("#F8FAFC"),0));edge.GradientStops.Add(new GradientStop(BrushColor("#64748B"),.45));edge.GradientStops.Add(new GradientStop(BrushColor("#E2E8F0"),1));labelBrush=Brush("#F1F5F9");
    }
    var border=new Border{Width=196,Height=54,CornerRadius=new CornerRadius(14),Background=shell,BorderBrush=edge,BorderThickness=new Thickness(1.4),ClipToBounds=true};
    var layer=new Grid{Clip=new RectangleGeometry(new Rect(0,0,196,54),14,14)};fill=new Border{Width=0,Height=50,Margin=new Thickness(2),CornerRadius=new CornerRadius(12),Background=fillGradient,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Opacity=galactic?.64:mythic?.7:.68};layer.Children.Add(fill);
    var shineBrush=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5)};shineBrush.GradientStops.Add(new GradientStop(Colors.Transparent,0));shineBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100,255,255,255),.5));shineBrush.GradientStops.Add(new GradientStop(Colors.Transparent,1));var shine=new System.Windows.Shapes.Rectangle{Width=38,Fill=shineBrush,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch,IsHitTestVisible=false,Opacity=.5};var move=new TranslateTransform(-45,0);shine.RenderTransform=move;var shineHost=new Grid{ClipToBounds=true};shineHost.Children.Add(shine);
    var grid=new Grid{Margin=new Thickness(9,5,11,5)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var labelPanel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var iconKey=galactic?"GalacticCard":mythic?"MythicCard":"BeskarCard";var logo=DroidImage(iconKey,32);if(logo!=null){logo.Margin=new Thickness(0,0,5,0);labelPanel.Children.Add(logo);}var name=Text(label,10,labelBrush,FontWeights.Bold);name.VerticalAlignment=VerticalAlignment.Center;labelPanel.Children.Add(name);grid.Children.Add(labelPanel);value=Text("00:00",18,Brush("#FFFFFF"),FontWeights.Bold);value.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(value,1);grid.Children.Add(value);layer.Children.Add(grid);border.Child=layer;return border;
  }
  Color BrushColor(string hex){return (Color)ColorConverter.ConvertFromString(hex);}

  Button PopupClose(Window w){var b=new Button{Content="X",Width=30,Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Background=Brush("#25334B"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};b.Click+=delegate{w.Close();};return b;}
  Dictionary<string,object> CurrentMember(){if(LastData==null||!LastData.ContainsKey("members"))return null;return L(LastData["members"]).Select(D).FirstOrDefault(m=>m!=null&&S(m,"id")==S(User,"id"));}
  List<Dictionary<string,object>> SearchNeeds(Dictionary<string,object> member){var old=ShowCompleted;ShowCompleted=true;var rows=Needs(member,Int32.MaxValue);ShowCompleted=old;return rows;}
  List<string> LimitedDealDroids(){return Catalog.Select(D).Where(c=>c!=null).SelectMany(c=>L(c.ContainsKey("rows")?c["rows"]:null).Select(D).Where(r=>r!=null)).SelectMany(r=>L(r.ContainsKey("requirements")?r["requirements"]:null).Select(D).Where(q=>q!=null)).SelectMany(q=>L(q.ContainsKey("droids")?q["droids"]:null).Select(Convert.ToString)).Where(x=>!String.IsNullOrWhiteSpace(x)).GroupBy(Norm).Select(g=>g.First()).OrderBy(x=>x).ToList();}
  long DealReset(Dictionary<string,object> d){long v=0;if(d!=null&&d.ContainsKey("nextResetAt"))Int64.TryParse(Convert.ToString(d["nextResetAt"]),out v);return v;}
  string DealTime(Dictionary<string,object> d){var left=Math.Max(0,DealReset(d)-(DateTimeOffset.UtcNow+ServerClockOffset).ToUnixTimeSeconds());return String.Format("{0:00}:{1:00}",left/60,left%60);}
  string DealSignature(Dictionary<string,object> d){return d==null?"":S(d,"hourKey")+"|"+S(d,"currentDroid")+"|"+S(d,"currentTier")+"|"+S(d,"passed");}
  void FetchInvitations(){
    LastInviteFetch=DateTime.UtcNow;if(User==null)return;var uid=S(User,"id");
    Task.Factory.StartNew(delegate{return D(Get("/users/"+Uri.EscapeDataString(uid)+"/invitations?t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));}).ContinueWith(t=>Dispatcher.Invoke(delegate{
      if(t.IsFaulted||t.Result==null)return;var incoming=L(t.Result.ContainsKey("invitations")?t.Result["invitations"]:null).Select(D).Where(x=>x!=null).ToList();
      bool hasNew=incoming.Any(inv=>!SeenInviteIds.Contains(S(inv,"id")));foreach(var inv in incoming)SeenInviteIds.Add(S(inv,"id"));PendingInvites=incoming;if(hasNew&&User!=null){Locked=false;SetClickThrough(false);Render();}
    }));
  }
  void RespondInvitation(Dictionary<string,object> invite,string action){
    if(invite==null||User==null)return;var id=S(invite,"id");PendingInvites.RemoveAll(x=>S(x,"id")==id);Render();
    Task.Factory.StartNew(delegate{return Request("/invitations/"+Uri.EscapeDataString(id),"POST",new Dictionary<string,object>{{"userId",S(User,"id")},{"action",action}});}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted){PendingInvites.Add(invite);Render();}else{LastRefresh=DateTime.MinValue;RefreshData();}}));
  }
  void AddInvitationNotices(){
    foreach(var inv in PendingInvites.Take(3)){var from=inv.ContainsKey("from")?D(inv["from"]):null;var group=inv.ContainsKey("group")?D(inv["group"]):null;var bar=new Border{Width=466,Height=66,HorizontalAlignment=HorizontalAlignment.Right,CornerRadius=new CornerRadius(12),BorderBrush=Brush("#7CA78BFA"),BorderThickness=new Thickness(1),Background=Brush("#F20A1323"),Margin=new Thickness(0,0,0,7),Padding=new Thickness(10,7,10,7)};var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});var copy=new StackPanel();copy.Children.Add(Text("GROUP INVITE",10,Brush("#C4B5FD"),FontWeights.ExtraBold));var detail=Text((from==null?"SOMEONE":S(from,"name").ToUpperInvariant())+" INVITED YOU TO "+(group==null?"A GROUP":S(group,"name").ToUpperInvariant()),10,Brush("#FFFFFF"),FontWeights.Bold);detail.TextTrimming=TextTrimming.CharacterEllipsis;copy.Children.Add(detail);row.Children.Add(copy);var accept=new Button{Content="ACCEPT",Margin=new Thickness(5,3,3,3),Background=Brush("#166534"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#4ADE80"),BorderThickness=new Thickness(1),FontSize=8,FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};accept.Click+=delegate{RespondInvitation(inv,"accept");};Grid.SetColumn(accept,1);row.Children.Add(accept);var reject=new Button{Content="REJECT",Margin=new Thickness(3),Background=Brush("#7F1D1D"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#F87171"),BorderThickness=new Thickness(1),FontSize=8,FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};reject.Click+=delegate{RespondInvitation(inv,"reject");};Grid.SetColumn(reject,2);row.Children.Add(reject);bar.Child=row;HudContent.Children.Add(bar);}
  }
  void FetchLimitedDeal(){LastLimitedDealFetch=DateTime.UtcNow;Task.Factory.StartNew(delegate{return D(Get("/limited-deal"));}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted||t.Result==null)return;var deal=t.Result;var sig=DealSignature(deal);var droid=S(deal,"currentDroid");var changed=!String.IsNullOrWhiteSpace(LastLimitedDealSignature)&&sig!=LastLimitedDealSignature;var firstKnown=String.IsNullOrWhiteSpace(LastLimitedDealSignature)&&!String.IsNullOrWhiteSpace(droid);LimitedDeal=deal;LastLimitedDealSignature=sig;if((changed||firstKnown)&&!String.IsNullOrWhiteSpace(droid))ShowLimitedDealAnnouncement(deal);}));}
  FrameworkElement DealArtwork(string droid,string tier,double size){var image=DroidImage(Key(droid)+"_"+Key(tier),size)??DroidImage(Key(droid),size);if(image!=null)return image;var fallback=Text("?",size*.55,Brush("#A78BFA"),FontWeights.ExtraBold);fallback.Width=size;fallback.Height=size;fallback.TextAlignment=TextAlignment.Center;fallback.VerticalAlignment=VerticalAlignment.Center;return fallback;}
  void ShowLimitedDealAnnouncement(Dictionary<string,object> deal){if(deal==null||String.IsNullOrWhiteSpace(S(deal,"currentDroid")))return;LimitedDealNotice=deal;LimitedDealNoticeUntil=DateTime.UtcNow.AddSeconds(8);if(User!=null)Render();}

  void ShowLimitedDealReporter(){ToggleVendorMode();}


  void ToggleVendorMode(){VendorMode=!VendorMode;SearchMode=false;Page=0;if(SearchBox!=null)SearchBox.Visibility=Visibility.Collapsed;if(SearchClose!=null)SearchClose.Visibility=Visibility.Collapsed;Render();}
  void AddLimitedDealNotice(){if(LimitedDealNotice==null||DateTime.UtcNow>=LimitedDealNoticeUntil)return;var droid=S(LimitedDealNotice,"currentDroid");var tier=S(LimitedDealNotice,"currentTier");var bar=new Border{Width=466,Height=66,HorizontalAlignment=HorizontalAlignment.Right,CornerRadius=new CornerRadius(14),BorderBrush=Brush("#665BDBFF"),BorderThickness=new Thickness(1),Background=Brush("#E6121A2B"),Margin=new Thickness(0,0,0,8),Padding=new Thickness(7,5,9,5)};var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(54)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});var artwork=DealArtwork(droid,tier,50);artwork.HorizontalAlignment=HorizontalAlignment.Center;artwork.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(artwork,0);row.Children.Add(artwork);var copy=new StackPanel{Margin=new Thickness(6,3,0,0)};copy.Children.Add(Text("LIMITED DEAL CONFIRMED",9,Brush("#FDE047"),FontWeights.ExtraBold));var line=Text(droid.ToUpperInvariant()+"  |  "+tier.ToUpperInvariant()+"  |  "+DealTime(LimitedDealNotice),13,TierBrush(tier),FontWeights.ExtraBold);line.TextTrimming=TextTrimming.CharacterEllipsis;copy.Children.Add(line);Grid.SetColumn(copy,1);row.Children.Add(copy);var x=new Button{Content="X",Width=22,Height=22,Padding=new Thickness(0),Background=Brush("#26334A"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontSize=8,Cursor=Cursors.Hand};x.Click+=delegate{LimitedDealNotice=null;Render();};Grid.SetColumn(x,2);row.Children.Add(x);bar.Child=row;HudContent.Children.Add(bar);}
  Style VendorComboItemStyle(){var style=new Style(typeof(ComboBoxItem));style.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#101C30")));style.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#F4F7FC")));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,7,10,7)));style.Setters.Add(new Setter(Control.FontSizeProperty,11.0));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));return style;}
  FrameworkElement DealPlaceholder(double size){var q=Text("?",size*.48,Brush("#778AA8"),FontWeights.Bold);q.Width=size;q.Height=size;q.TextAlignment=TextAlignment.Center;q.HorizontalAlignment=HorizontalAlignment.Center;q.VerticalAlignment=VerticalAlignment.Center;q.Padding=new Thickness(0,size*.18,0,0);return q;}
  void ShowVendorDroidPicker(List<string> droids,Action<string> choose){
    var w=new Window{Title="Choose Limited Deal droid",Width=460,Height=540,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#091324"),Foreground=Brush("#FFFFFF"),Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};var root=new Grid{Margin=new Thickness(18)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());var header=new Grid{Margin=new Thickness(2,0,2,12)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});header.Children.Add(Text("CHOOSE LIMITED DEAL DROID",18,Brush("#FFFFFF"),FontWeights.ExtraBold));var close=new Button{Content="X",Width=30,Height=28,Background=Brush("#991B1B"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#F87171"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};close.Click+=delegate{w.Close();};Grid.SetColumn(close,1);header.Children.Add(close);root.Children.Add(header);var wrap=new WrapPanel{Orientation=Orientation.Horizontal};foreach(var item in droids){var droid=item;var body=new Grid{Margin=new Thickness(9,5,9,5)};var label=Text(droid.ToUpperInvariant(),11,Brush("#FFFFFF"),FontWeights.Bold);label.VerticalAlignment=VerticalAlignment.Center;label.HorizontalAlignment=HorizontalAlignment.Left;label.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(label);var button=new Button{Content=body,Width=196,Height=42,Margin=new Thickness(3),Padding=new Thickness(3),Background=Brush("#14233A"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#344B6D"),BorderThickness=new Thickness(1),Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Stretch};button.Click+=delegate{if(choose!=null)choose(droid);w.Close();};wrap.Children.Add(button);}var scroll=new ScrollViewer{Content=wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);w.Content=root;w.ShowDialog();
  }
  void RenderVendorInline(){
    var deal=LimitedDeal;var currentDroid=deal==null?"":S(deal,"currentDroid");var tier=deal==null?"Default":S(deal,"currentTier");var shell=new Border{Width=466,HorizontalAlignment=HorizontalAlignment.Right,CornerRadius=new CornerRadius(16),BorderBrush=Brush("#604C74C9"),BorderThickness=new Thickness(1),Background=Brush("#F00A1323"),Padding=new Thickness(16)};var panel=new StackPanel();var top=new Grid();top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});top.Children.Add(Text("SECRET VENDOR  |  LIMITED DEAL",13,Brush("#FDE047"),FontWeights.ExtraBold));var close=new Button{Content="X",Width=24,Height=22,Padding=new Thickness(0),Background=Brush("#991B1B"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#F87171"),BorderThickness=new Thickness(1),FontSize=8,Cursor=Cursors.Hand};close.Click+=delegate{ToggleVendorMode();};Grid.SetColumn(close,1);top.Children.Add(close);panel.Children.Add(top);
    var current=new Grid{Margin=new Thickness(0,12,0,12)};current.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(94)});current.ColumnDefinitions.Add(new ColumnDefinition());var stage=new Border{Width=86,Height=86,CornerRadius=new CornerRadius(14),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=Brush("#17233B"),BorderBrush=Brush("#3B4E6C"),BorderThickness=new Thickness(1),Child=String.IsNullOrWhiteSpace(currentDroid)?DealPlaceholder(84):DealArtwork(currentDroid,tier,84)};current.Children.Add(stage);var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};var threshold=deal!=null&&deal.ContainsKey("threshold")?Convert.ToString(deal["threshold"]):"2";info.Children.Add(Text(String.IsNullOrWhiteSpace(currentDroid)?"VOTE FOR THE LIMITED DEAL":currentDroid.ToUpperInvariant(),17,String.IsNullOrWhiteSpace(currentDroid)?Brush("#FFFFFF"):TierBrush(tier),FontWeights.ExtraBold));var description=String.IsNullOrWhiteSpace(currentDroid)?"Pick the droid and tier you see. "+threshold+" matching votes confirm it.":tier.ToUpperInvariant()+"  |  RESETS IN "+DealTime(deal);var desc=Text(description,10,Brush("#91A6C6"),FontWeights.Bold);desc.TextWrapping=TextWrapping.Wrap;desc.MaxWidth=310;info.Children.Add(desc);Grid.SetColumn(info,1);current.Children.Add(info);panel.Children.Add(current);
    if(String.IsNullOrWhiteSpace(currentDroid)){
      var droidList=LimitedDealDroids();string selectedDroid=droidList.FirstOrDefault(x=>Norm(x)==Norm(VendorSelectedDroid))??"",selectedTier=VendorSelectedTier;VendorSelectedDroid=selectedDroid;var form=new StackPanel();var droidLabel=Text("DROID",9,Brush("#AFC2DF"),FontWeights.ExtraBold);droidLabel.Margin=new Thickness(0,0,0,4);form.Children.Add(droidLabel);var selectDroid=new ComboBox{ItemsSource=droidList,SelectedItem=String.IsNullOrWhiteSpace(selectedDroid)?null:selectedDroid,Height=42,Background=Brush("#14233A"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#526985"),BorderThickness=new Thickness(1),FontSize=11,FontWeight=FontWeights.Bold,IsTextSearchEnabled=true,MaxDropDownHeight=280,ItemContainerStyle=VendorComboItemStyle()};form.Children.Add(selectDroid);var tierLabel=Text("TIER",9,Brush("#AFC2DF"),FontWeights.ExtraBold);tierLabel.Margin=new Thickness(0,11,0,4);form.Children.Add(tierLabel);var tierRow=new System.Windows.Controls.Primitives.UniformGrid{Rows=1,Columns=5};var tierButtons=new List<Button>();Action paintTiers=delegate{foreach(var button in tierButtons){var active=Convert.ToString(button.Tag)==selectedTier;button.Background=active?Brush("#6D45C7"):Brush("#14233A");button.BorderBrush=active?Brush("#C4B5FD"):Brush("#3B4E6C");button.Foreground=Brush("#FFFFFF");}};foreach(var value in new[]{"Default","Gold","Diamond","Rainbow","Beskar"}){var tierName=value;var button=new Button{Content=tierName.ToUpperInvariant(),Tag=tierName,Height=38,Margin=new Thickness(2),Padding=new Thickness(2),Background=Brush("#14233A"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#3B4E6C"),BorderThickness=new Thickness(1),FontSize=8,FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};button.Click+=delegate{selectedTier=tierName;VendorSelectedTier=tierName;paintTiers();stage.Child=String.IsNullOrWhiteSpace(selectedDroid)?DealPlaceholder(84):DealArtwork(selectedDroid,selectedTier,84);};tierButtons.Add(button);tierRow.Children.Add(button);}paintTiers();form.Children.Add(tierRow);var voteStatus=Text("Votes are anonymous. You can update yours until the deal locks.",9,Brush("#8FA6C7"),FontWeights.SemiBold);voteStatus.Margin=new Thickness(0,9,0,7);voteStatus.TextWrapping=TextWrapping.Wrap;form.Children.Add(voteStatus);var report=new Button{Content="SUBMIT VOTE",Height=42,Background=Brush("#6D45C7"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#A78BFA"),BorderThickness=new Thickness(1),FontSize=11,FontWeight=FontWeights.ExtraBold,Cursor=Cursors.Hand};form.Children.Add(report);selectDroid.SelectionChanged+=delegate{var picked=Convert.ToString(selectDroid.SelectedItem);if(String.IsNullOrWhiteSpace(picked))return;selectedDroid=picked;VendorSelectedDroid=picked;stage.Child=DealArtwork(picked,selectedTier,84);voteStatus.Text="Ready to submit "+picked+" ("+selectedTier+").";voteStatus.Foreground=Brush("#AFC2DF");};report.Click+=delegate{if(String.IsNullOrWhiteSpace(selectedDroid)){voteStatus.Text="Choose a droid first.";voteStatus.Foreground=Brush("#FCA5A5");return;}report.IsEnabled=false;report.Content="SUBMITTING...";voteStatus.Text="Sending your vote...";voteStatus.Foreground=Brush("#FDE68A");var sendDroid=selectedDroid;var sendTier=selectedTier;Task.Factory.StartNew(delegate{return D(Request("/limited-deal/vote","POST",new Dictionary<string,object>{{"userId",S(User,"id")},{"droid",sendDroid},{"tier",sendTier}}));}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted||t.Result==null){report.Content="RETRY VOTE";report.IsEnabled=true;var voteError=t.Exception!=null&&t.Exception.InnerException!=null?t.Exception.InnerException.Message:"Vote failed to send.";voteStatus.Text=voteError;voteStatus.Foreground=Brush("#FCA5A5");return;}var response=t.Result;var newDeal=response.ContainsKey("deal")?D(response["deal"]):response;LimitedDeal=newDeal;LastLimitedDealSignature=DealSignature(newDeal);if(newDeal!=null&&!String.IsNullOrWhiteSpace(S(newDeal,"currentDroid"))){VendorMode=false;ShowLimitedDealAnnouncement(newDeal);}else{report.Content="UPDATE VOTE";report.IsEnabled=true;voteStatus.Text="Vote counted. Waiting for another matching report.";voteStatus.Foreground=Brush("#86EFAC");}}));};panel.Children.Add(form);
    }
    shell.Child=panel;HudContent.Children.Add(shell);SetOverlayLogicalHeight(String.IsNullOrWhiteSpace(currentDroid)?420:245);AnchorToFortnite();
  }

  void ShowDroidSearch(){
    var member=CurrentMember();if(member==null)return;var w=new Window{Title="Search droids",Width=690,Height=540,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#091324"),Foreground=Brush("#FFFFFF"),Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
    var root=new Grid{Margin=new Thickness(18)};root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(36)});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(48)});root.RowDefinitions.Add(new RowDefinition());var title=Text("SEARCH REBIRTH DROIDS",18,Brush("#FFFFFF"),FontWeights.Bold);title.VerticalAlignment=VerticalAlignment.Center;root.Children.Add(title);root.Children.Add(PopupClose(w));
    var search=LoginTextBox();search.Margin=new Thickness(0,5,0,5);search.ToolTip="Search by droid, tier, or Rebirth row";Grid.SetRow(search,1);root.Children.Add(search);var results=new WrapPanel{Orientation=Orientation.Horizontal};var scroll=new ScrollViewer{Content=results,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,2);root.Children.Add(scroll);w.Content=root;
    Action render=null;render=delegate{results.Children.Clear();var q=Norm(search.Text);foreach(var need in SearchNeeds(member).Where(n=>q.Length==0||Norm(S(n,"Name")+" "+S(n,"Tier")+" "+S(n,"RB")).Contains(q)).Take(60)){var tile=NeedTile(need,true,true,member);tile.Margin=new Thickness(8,5,8,8);var button=tile as Button;if(button!=null)button.Click+=delegate{Dispatcher.BeginInvoke(new Action(render));};results.Children.Add(tile);}if(results.Children.Count==0)results.Children.Add(Text("No matching droids.",12,Brush("#9FB0C9"),FontWeights.SemiBold));};
    search.TextChanged+=delegate{render();};render();w.Show();search.Focus();
  }
  void ShowGeneralChat(){
    var w=new Window{Title="DroidTrakr General Chat",Width=640,Height=570,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#091324"),Foreground=Brush("#FFFFFF"),Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
    var root=new Grid{Margin=new Thickness(18)};root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(38)});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(54)});var title=Text("DROIDTRAKR GENERAL CHAT",18,Brush("#FFFFFF"),FontWeights.Bold);title.VerticalAlignment=VerticalAlignment.Center;root.Children.Add(title);root.Children.Add(PopupClose(w));
    var messages=new StackPanel();var scroll=new ScrollViewer{Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Brush("#101A2B"),Padding=new Thickness(12)};Grid.SetRow(scroll,1);root.Children.Add(scroll);
    var entryRow=new Grid{Margin=new Thickness(0,10,0,0)};entryRow.ColumnDefinitions.Add(new ColumnDefinition());entryRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});var input=LoginTextBox();input.Margin=new Thickness(0);input.ToolTip="Message general chat";entryRow.Children.Add(input);var send=new Button{Content="SEND",Margin=new Thickness(8,0,0,0),Background=Brush("#6D45C7"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};Grid.SetColumn(send,1);entryRow.Children.Add(send);Grid.SetRow(entryRow,2);root.Children.Add(entryRow);w.Content=root;
    Action load=delegate{Task.Factory.StartNew(delegate{return D(Get("/chat?since=0&t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted||t.Result==null)return;messages.Children.Clear();foreach(var m in L(t.Result.ContainsKey("messages")?t.Result["messages"]:null).Select(D).Where(x=>x!=null)){var line=new StackPanel{Margin=new Thickness(0,0,0,10)};line.Children.Add(Text(S(m,"player"),11,Brush("#A78BFA"),FontWeights.Bold));line.Children.Add(Text(S(m,"text"),12,Brush("#E5ECF7"),FontWeights.Normal));messages.Children.Add(line);}scroll.ScrollToEnd();}));};
    Action sendMessage=delegate{var text=input.Text.Trim();if(text.Length==0)return;input.Clear();send.IsEnabled=false;Task.Factory.StartNew(delegate{return Request("/chat","POST",new Dictionary<string,object>{{"text",text},{"clientId","ov"+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}});}).ContinueWith(t=>Dispatcher.Invoke(delegate{send.IsEnabled=true;load();}));};send.Click+=delegate{sendMessage();};input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter){sendMessage();e.Handled=true;}};var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};timer.Tick+=delegate{load();};w.Closed+=delegate{timer.Stop();};w.Show();load();timer.Start();input.Focus();
  }

  void ShowCycleManager() {
    if(User==null)return; var me=LastData==null?null:L(LastData.ContainsKey("members")?LastData["members"]:null).Select(D).FirstOrDefault(x=>x!=null&&S(x,"id")==S(User,"id"));
    var w=new Window{Title="Rebirth Cycle",Width=320,Height=355,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brush("#091324"),Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};var root=new StackPanel{Margin=new Thickness(22)};var header=new Grid{Margin=new Thickness(0,0,0,15)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});var title=Text("SELECT REBIRTH CYCLE",18,Brush("#FFFFFF"),FontWeights.Bold);header.Children.Add(title);var close=new Button{Content="X",Width=26,Height=26,Padding=new Thickness(0),Background=Brush("#7F1D2D"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};close.Click+=delegate{w.Close();};Grid.SetColumn(close,1);header.Children.Add(close);root.Children.Add(header);
    foreach(var c in Catalog.Select(D).Where(x=>x!=null)){var id=S(c,"id");var b=new Button{Content=id,Height=38,Margin=new Thickness(0,0,0,7),Background=me!=null&&S(me,"active")==id?Brush("#6D45C7"):Brush("#172338"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold};b.Click+=delegate{if(me==null)return;var previous=S(me,"active");var checks=me.ContainsKey("checks")?D(me["checks"]):new Dictionary<string,object>();me["active"]=id;Page=0;LastRefresh=DateTime.UtcNow;w.Close();Render();Task.Factory.StartNew(delegate{return Request("/users/"+Uri.EscapeDataString(S(User,"id"))+"/rebirth","POST",new Dictionary<string,object>{{"active",id},{"checks",checks},{"source","overlay"},{"clientUpdatedAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}});}).ContinueWith(t=>Dispatcher.Invoke(delegate{if(t.IsFaulted&&S(me,"active")==id){me["active"]=previous;Status.Text="CYCLE SAVE FAILED";Render();}else if(!t.IsFaulted){Status.Text="SAVED";}}));};root.Children.Add(b);}
    var reset=new Button{Content="RESET ACTIVE CYCLE",Height=40,Margin=new Thickness(0,8,0,0),Background=Brush("#7F1D2D"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#F87171"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand,ToolTip="Clear all checked droids and credit boxes for the active Rebirth cycle only"};reset.Click+=delegate{if(me==null)return;var active=S(me,"active");if(String.IsNullOrWhiteSpace(active))return;var answer=MessageBox.Show(w,"Reset "+active+"?\n\nThis will clear all checked droids and credit boxes for this Rebirth cycle only.","Confirm cycle reset",MessageBoxButton.YesNo,MessageBoxImage.Warning);if(answer!=MessageBoxResult.Yes)return;var checks=me.ContainsKey("checks")?D(me["checks"]):null;if(checks==null){checks=new Dictionary<string,object>();me["checks"]=checks;}RememberUndo(me,"cycle reset");foreach(var key in checks.Keys.Where(k=>{var parts=(k??"").Split(':');return parts.Length>0&&Norm(parts[0])==Norm(active);}).ToList())checks.Remove(key);Page=0;LastRefresh=DateTime.UtcNow;Status.Text="RESETTING "+active+"...";w.Close();Render();QueueRebirthSave(me);};root.Children.Add(reset);w.Content=root;w.ShowDialog();
  }

  void ShowGroupManager() {
    if (User == null) return; var w = new Window { Title="DroidTrakr Groups", Width=420, Height=520, WindowStyle=WindowStyle.None, ResizeMode=ResizeMode.NoResize, Background=Brush("#091324"), Foreground=Brush("#FFFFFF"), Topmost=true, WindowStartupLocation=WindowStartupLocation.CenterScreen };
    var root=new Grid { Margin=new Thickness(22) }; root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
    var title=Text("TRACKER GROUPS",20,Brush("#FFFFFF"),FontWeights.Bold);root.Children.Add(title);var popupClose=new Button{Content="X",Width=28,Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(0),Background=Brush("#7F1D2D"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold,Cursor=Cursors.Hand};popupClose.Click+=delegate{w.Close();};root.Children.Add(popupClose);var list=new StackPanel { Margin=new Thickness(0,18,0,15) };var scroll=new ScrollViewer { Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };Grid.SetRow(scroll,1);root.Children.Add(scroll);
    Action draw=delegate { list.Children.Clear(); foreach(var g in AvailableGroups.Where(x => !B(x,"isPersonal") && (!String.IsNullOrWhiteSpace(S(x,"ownerUserId")) || Convert.ToInt32(x.ContainsKey("memberCount")?x["memberCount"]:0)>0))){var item=new Button { Content=S(g,"name"),Height=38,Margin=new Thickness(0,0,0,6),Background=S(SelectedGroup,"id")==S(g,"id")?Brush("#6D45C7"):Brush("#172338"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,0,12,0) };item.Click+=delegate { SelectedGroup=g;SavePreferredGroup(S(g,"id"));Page=0;w.Close();RefreshData(); };list.Children.Add(item);} if(LastData!=null&&SelectedGroup!=null){var cap=Text("CURRENT MEMBERS",10,Brush("#8294B1"),FontWeights.Bold);cap.Margin=new Thickness(0,10,0,6);list.Children.Add(cap);foreach(var m in L(LastData.ContainsKey("members")?LastData["members"]:null).Select(D).Where(x=>x!=null&&S(x,"id")!=S(User,"id"))){var row=new Grid{Height=34,Margin=new Thickness(0,0,0,4)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});var nm=Text(S(m,"name"),12,Brush("#FFFFFF"),FontWeights.SemiBold);nm.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(nm);var rem=new Button{Content="REMOVE",Background=Brush("#7F1D2D"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),FontSize=9};Grid.SetColumn(rem,1);row.Children.Add(rem);var target=m;rem.Click+=delegate{rem.IsEnabled=false;Task.Factory.StartNew(delegate{return Request("/groups/"+Uri.EscapeDataString(S(SelectedGroup,"id"))+"/remove","POST",new Dictionary<string,object>{{"targetUserId",S(target,"id")}});}).ContinueWith(t=>w.Dispatcher.Invoke(delegate{if(t.IsFaulted){rem.Content="FAILED";rem.IsEnabled=true;}else{w.Close();LastRefresh=DateTime.MinValue;RefreshData();}}));};list.Children.Add(row);}} } ;draw();
    var create=new Grid { Margin=new Thickness(0,10,0,0) };create.ColumnDefinitions.Add(new ColumnDefinition());create.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(92) });var name=new TextBox { Height=34,Background=Brush("#101C30"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#2A3A55"),Padding=new Thickness(8),FontSize=12 };create.Children.Add(name);var add=new Button { Content="CREATE",Margin=new Thickness(8,0,0,0),Background=Brush("#6D45C7"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0) };Grid.SetColumn(add,1);create.Children.Add(add);Grid.SetRow(create,2);root.Children.Add(create);
    add.Click+=delegate {var n=name.Text.Trim();if(n.Length==0)return;add.IsEnabled=false;Task.Factory.StartNew(delegate{return D(Request("/groups","POST",new Dictionary<string,object>{{"name",n}}));}).ContinueWith(t=>w.Dispatcher.Invoke(delegate{if(t.IsFaulted){add.Content="FAILED";add.IsEnabled=true;return;}var result=t.Result;SelectedGroup=result!=null&&result.ContainsKey("group")?D(result["group"]):null;w.Close();LastRefresh=DateTime.MinValue;RefreshData();}));};
    var invite=new Grid { Margin=new Thickness(0,10,0,0) };invite.ColumnDefinitions.Add(new ColumnDefinition());invite.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(92)});var member=new TextBox{Height=34,Background=Brush("#101C30"),Foreground=Brush("#FFFFFF"),BorderBrush=Brush("#2A3A55"),Padding=new Thickness(8),FontSize=12,ToolTip="Registered DroidTrakr username"};invite.Children.Add(member);var send=new Button{Content="INVITE",Margin=new Thickness(8,0,0,0),Background=Brush("#2563A8"),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0)};Grid.SetColumn(send,1);invite.Children.Add(send);Grid.SetRow(invite,3);root.Children.Add(invite);
    send.Click+=delegate{var username=member.Text.Trim();if(username.Length==0||SelectedGroup==null)return;send.IsEnabled=false;Task.Factory.StartNew(delegate{return Request("/groups/"+Uri.EscapeDataString(S(SelectedGroup,"id"))+"/invite","POST",new Dictionary<string,object>{{"username",username}});}).ContinueWith(t=>w.Dispatcher.Invoke(delegate{if(t.IsFaulted){send.Content="FAILED";send.IsEnabled=true;}else{send.Content="INVITED";member.Text="";send.IsEnabled=true;}}));};w.Content=root;w.ShowDialog();
  }

  void SendOverlayHeartbeat() {
    if(User==null)return;LastHeartbeatAttempt=DateTime.UtcNow;var uid=S(User,"id");Task.Factory.StartNew(delegate{try{Request("/users/"+Uri.EscapeDataString(uid)+"/overlay-heartbeat","POST",new Dictionary<string,object>());return true;}catch{return false;} }).ContinueWith(t=>Dispatcher.Invoke(delegate{if(!t.IsFaulted&&t.Result)LastHeartbeat=DateTime.UtcNow;}));
  }

  void RefreshData() {
    if(User==null)return;if(Busy){RefreshQueued=true;return;}Busy=true;Status.Text="SYNCING DROIDTRAKR...";
    Task.Factory.StartNew(delegate {
      var uid = S(User, "id"); var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(); var groupResponse = D(Get("/users/" + Uri.EscapeDataString(uid) + "/groups?t=" + stamp));
      var groups = groupResponse != null && groupResponse.ContainsKey("groups") ? L(groupResponse["groups"]).Select(D).Where(x => x != null).ToList() : new List<Dictionary<string, object>>();
      var preferred=LoadPreferredGroup();var wanted=SelectedGroup!=null?S(SelectedGroup,"id"):preferred;var group=!String.IsNullOrWhiteSpace(wanted)?groups.FirstOrDefault(q=>S(q,"id")==wanted):null;if(group==null&&groups.Count==0&&SelectedGroup!=null&&S(SelectedGroup,"id")==wanted)group=SelectedGroup;group = group ?? groups.FirstOrDefault(q => !B(q, "isPersonal")) ?? groups.FirstOrDefault();
      var gid = group != null ? S(group, "id") : (!String.IsNullOrWhiteSpace(wanted)?wanted:S(User, "groupId"));
      if (String.IsNullOrWhiteSpace(gid)) throw new Exception("Your account has no authorized DroidTrakr tracker group yet");
      var data = D(Get("/groups/" + Uri.EscapeDataString(gid) + "/rebirth-summary?t=" + stamp));
      return new object[] { data, group, gid, groups };
    }).ContinueWith(t => Dispatcher.Invoke(delegate {
      if (t.IsFaulted) { Busy=false;Status.Text = t.Exception.InnerException != null ? t.Exception.InnerException.Message : t.Exception.Message;if(RefreshQueued){RefreshQueued=false;Dispatcher.BeginInvoke(new Action(RefreshData));}return; }
      var values = t.Result; var incoming=values[0] as Dictionary<string,object>;
      if(RebirthSavePending&&PendingSaveMember!=null&&incoming!=null&&incoming.ContainsKey("members")){
        var incomingMe=L(incoming["members"]).Select(D).FirstOrDefault(m=>m!=null&&S(m,"id")==S(User,"id"));
        if(incomingMe!=null){var localChecks=PendingSaveMember.ContainsKey("checks")?D(PendingSaveMember["checks"]):null;if(localChecks!=null)incomingMe["checks"]=new Dictionary<string,object>(localChecks);incomingMe["active"]=S(PendingSaveMember,"active");PendingSaveMember=incomingMe;}
      }
      var group=values[1] as Dictionary<string,object>;LastData=incoming;SelectedGroup=group;AvailableGroups=values.Length>3?values[3] as List<Dictionary<string,object>>:AvailableGroups;LastRefresh=DateTime.UtcNow;Status.Text=group!=null&&!String.IsNullOrWhiteSpace(S(group,"name"))?S(group,"name"):"PERSONAL TRACKER";Busy=false;Render();if(RefreshQueued){RefreshQueued=false;Dispatcher.BeginInvoke(new Action(RefreshData));}
    }));
  }

  void FocusSearchInput(){if(!SearchMode||SearchBox==null)return;SetClickThrough(false);ShowActivated=true;Show();var foreground=GetForegroundWindow();uint pid;var foregroundThread=foreground==IntPtr.Zero?0:GetWindowThreadProcessId(foreground,out pid);var currentThread=GetCurrentThreadId();var attached=foregroundThread!=0&&foregroundThread!=currentThread&&AttachThreadInput(currentThread,foregroundThread,true);try{BringWindowToTop(Hwnd);SetForegroundWindow(Hwnd);SetActiveWindow(Hwnd);Activate();Focus();SearchBox.IsEnabled=true;SearchBox.Focusable=true;FocusManager.SetFocusedElement(this,SearchBox);SearchBox.Focus();Keyboard.Focus(SearchBox);SearchBox.CaretIndex=SearchBox.Text.Length;}finally{if(attached)AttachThreadInput(currentThread,foregroundThread,false);}}
  void ToggleSearchMode(){SearchMode=!SearchMode;Page=0;if(SearchBox!=null){SearchBox.Visibility=SearchMode?Visibility.Visible:Visibility.Collapsed;if(SearchClose!=null)SearchClose.Visibility=SearchMode?Visibility.Visible:Visibility.Collapsed;if(!SearchMode)SearchBox.Clear();}Render();if(SearchMode&&SearchBox!=null){FocusSearchInput();Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(FocusSearchInput));var retry=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};int attempts=0;retry.Tick+=delegate{attempts++;FocusSearchInput();if(attempts>=2)retry.Stop();};retry.Start();}else if(Locked)SetClickThrough(true);}
  void ToggleMode() { SearchMode=false;VendorMode=false;if(SearchBox!=null)SearchBox.Visibility=Visibility.Collapsed;if(SearchClose!=null)SearchClose.Visibility=Visibility.Collapsed;Mode = Mode == "mine" ? "overview" : "mine"; Page = 0; Render(); }
  void ChangePage(int delta) { Page = Math.Max(0, Page + delta); Render(); }
  void ToggleLock() { Locked = !Locked; if (Rail != null) Rail.Visibility = Locked ? Visibility.Collapsed : Visibility.Visible; SetClickThrough(Locked); }
  void SetClickThrough(bool enabled) { if (Hwnd == IntPtr.Zero) return; int ex = GetWindowLong(Hwnd, GWL_EXSTYLE); if (enabled) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED; else ex &= ~WS_EX_TRANSPARENT; SetWindowLong(Hwnd, GWL_EXSTYLE, ex); }

  void Render() {
    if (LastData == null || HudContent == null) return; HudContent.Children.Clear(); VisibleNeedTiles.Clear(); var members = LastData.ContainsKey("members") ? L(LastData["members"]).Select(D).Where(x => x != null).ToList() : new List<Dictionary<string, object>>();
    AddInvitationNotices();
    if(VendorMode){RenderVendorInline();return;}
    AddLimitedDealNotice();
    if(SearchMode){
      var me=members.FirstOrDefault(m=>S(m,"id")==S(User,"id"));if(me==null){AddMessage("Your member row was not returned.");return;}var q=Norm(SearchBox!=null?SearchBox.Text:"");var matches=SearchNeeds(me).Where(n=>q.Length==0||Norm(S(n,"Name")+" "+S(n,"Tier")+" "+S(n,"RB")).Contains(q)).ToList();var groups=matches.GroupBy(n=>S(n,"RB")).ToList();var pages=Math.Max(1,(int)Math.Ceiling(groups.Count/4.0));if(Page>=pages)Page=pages-1;var visibleGroups=groups.Skip(Page*4).Take(4).ToList();foreach(var rbGroup in visibleGroups){var line=new Grid{Margin=new Thickness(0,0,0,10)};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42)});var visible=rbGroup.Take(3).ToList();var wrap=new WrapPanel{Orientation=Orientation.Horizontal,Width=visible.Count*122,HorizontalAlignment=HorizontalAlignment.Right};foreach(var need in visible)wrap.Children.Add(NeedTile(need,true,true,me));line.Children.Add(wrap);var rb=Text(rbGroup.Key,12,Brush("#D3DEEF"),FontWeights.Bold);rb.HorizontalAlignment=HorizontalAlignment.Right;rb.VerticalAlignment=VerticalAlignment.Center;rb.Margin=new Thickness(4,0,2,16);rb.Effect=Shadow();Grid.SetColumn(rb,1);line.Children.Add(rb);HudContent.Children.Add(line);}if(visibleGroups.Count==0)AddMessage("No matching droids.");var rows=Math.Max(1,visibleGroups.Count);SetOverlayLogicalHeight(Math.Min(710,170+rows*132));AnchorToFortnite();return;
    }
    if (Mode == "mine") {
      var me = members.FirstOrDefault(m => S(m, "id") == S(User, "id")); var rbCount = me == null ? 0 : Needs(me, Int32.MaxValue).Select(n => S(n,"RB")).Distinct().Count(); var pages = Math.Max(1,(int)Math.Ceiling(rbCount/4.0)); if(Page>=pages)Page=pages-1;
      var visibleRows=Math.Min(4,Math.Max(1,rbCount-Page*4)); SetOverlayLogicalHeight(Math.Min(710,170+visibleRows*132));
      if (me != null) AddMember(me, true, true); else AddMessage("Your member row was not returned by this authorized group.");
    } else {
      var currentMembers = members.Where(m => B(m, "activeRecent") || S(m,"id")==S(User,"id")).OrderByDescending(m=>S(m,"id")==S(User,"id")).ToList(); var pages=Math.Max(1,(int)Math.Ceiling(currentMembers.Count/4.0)); if(Page>=pages)Page=pages-1; var pageMembers=currentMembers.Skip(Page*4).Take(4).ToList();
      SetOverlayLogicalHeight(Math.Max(210,55+pageMembers.Count*132));
      foreach (var member in pageMembers) AddMember(member, S(member, "id") == S(User, "id"), false);
      if (currentMembers.Count == 0) AddMessage("No current team members are available.");
    }
    AnchorToFortnite();
  }
  void AddMessage(string msg) { var t = Text(msg, 12, Brush("#D4DDEA"), FontWeights.SemiBold); t.Margin = new Thickness(0, 18, 0, 0); t.Effect = Shadow(); HudContent.Children.Add(t); }
  void AddMember(Dictionary<string, object> member, bool self, bool full) {
    var allNeeds = Needs(member, Int32.MaxValue); var container = new StackPanel { Margin = new Thickness(0,0,0,8) };
    if (!full) {
      var next = allNeeds.Take(3).ToList(); var line = new Grid { Width=466, Margin=new Thickness(0,0,0,10), HorizontalAlignment=HorizontalAlignment.Right };
      line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(366)}); line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});
      var wrap = new WrapPanel { Orientation=Orientation.Horizontal, Width=next.Count*122, HorizontalAlignment=HorizontalAlignment.Right }; foreach(var need in next) wrap.Children.Add(NeedTile(need,false,false,member)); Grid.SetColumn(wrap,0); line.Children.Add(wrap);
      if(next.Count==0){var complete=Text("COMPLETE",12,Brush("#72E3B8"),FontWeights.ExtraBold);complete.HorizontalAlignment=HorizontalAlignment.Right;complete.VerticalAlignment=VerticalAlignment.Center;complete.Margin=new Thickness(0,0,14,16);complete.Effect=Shadow();Grid.SetColumn(complete,0);line.Children.Add(complete);}
      var identity=new StackPanel{Width=100,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,0,16)};var label=Text(self?"YOU":S(member,"name"),11,Brush("#FFFFFF"),FontWeights.Bold);label.Width=98;label.TextAlignment=TextAlignment.Left;label.TextTrimming=TextTrimming.CharacterEllipsis;label.Effect=Shadow();identity.Children.Add(label);var cycle=Text(S(member,"active"),9,Brush("#91A6C6"),FontWeights.Bold);cycle.Width=98;cycle.TextAlignment=TextAlignment.Left;cycle.Margin=new Thickness(0,2,0,0);cycle.Effect=Shadow();identity.Children.Add(cycle);Grid.SetColumn(identity,1);line.Children.Add(identity);container.Children.Add(line);
    } else {
      var groups = allNeeds.GroupBy(n => S(n,"RB")).Skip(Page*4).Take(4).ToList();
      foreach(var rbGroup in groups){var line=new Grid{Margin=new Thickness(0,0,0,10)};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42)});var visible=rbGroup.Take(3).ToList();var wrap=new WrapPanel{Orientation=Orientation.Horizontal,Width=visible.Count*122,HorizontalAlignment=HorizontalAlignment.Right};foreach(var need in visible)wrap.Children.Add(NeedTile(need,true,self,member));line.Children.Add(wrap);var rb=Text(rbGroup.Key,12,Brush("#D3DEEF"),FontWeights.Bold);rb.HorizontalAlignment=HorizontalAlignment.Right;rb.VerticalAlignment=VerticalAlignment.Center;rb.Margin=new Thickness(4,0,2,16);rb.Effect=Shadow();Grid.SetColumn(rb,1);line.Children.Add(rb);container.Children.Add(line);}
    }
    if(allNeeds.Count==0&&full){var done=Text("COMPLETE",11,Brush("#72E3B8"),FontWeights.Bold);done.HorizontalAlignment=HorizontalAlignment.Right;done.Effect=Shadow();container.Children.Add(done);} HudContent.Children.Add(container);
  }
  FrameworkElement NeedTile(Dictionary<string, object> n, bool full, bool interactive, Dictionary<string, object> member) {
    var tile = new StackPanel { Width = 118, Margin = new Thickness(0, 0, 4, 8) };
    var artKey = Key(S(n, "Name")) + "_" + Key(S(n, "Tier")); var image = DroidImage(artKey, 92) ?? DroidImage(Key(S(n, "Name")), 92); if (image != null) { image.HorizontalAlignment = HorizontalAlignment.Center; tile.Children.Add(image); }
    var name = Text(S(n, "Name"), 12, TierBrush(S(n, "Tier")), FontWeights.Bold); name.Width = 118; name.TextAlignment = TextAlignment.Center; name.HorizontalAlignment = HorizontalAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis; tile.Children.Add(name);
    if(B(n,"Complete")){var badge=Text(B(n,"Sell")?"SELL":"DONE",9,B(n,"Sell")?Brush("#FDE047"):Brush("#72E3B8"),FontWeights.ExtraBold);badge.Width=118;badge.TextAlignment=TextAlignment.Center;badge.HorizontalAlignment=HorizontalAlignment.Center;badge.Effect=Shadow();tile.Children.Add(badge);if(image!=null)image.Opacity=.35;}
    var stage=new Grid{Width=122};FrameworkElement visual=tile;
    if(interactive){var hit = new Button { Content = tile, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = B(n,"Complete") ? "Re-enable this droid and sync to DroidTrakr" : "Mark complete and sync to DroidTrakr" };hit.Click += delegate { MarkComplete(member, n, hit); };visual=hit;}
    stage.Children.Add(visual);var visualKey=S(member,"id")+"|"+S(n,"CheckKey");if(!String.IsNullOrWhiteSpace(visualKey))VisibleNeedTiles[visualKey]=visual;return stage;
  }
  void StartPoof(FrameworkElement target){
    if(target==null)return;target.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1,1);target.RenderTransform=scale;var ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,1.12,TimeSpan.FromMilliseconds(140)){EasingFunction=ease});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.86,TimeSpan.FromMilliseconds(140)){EasingFunction=ease});target.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(160)){EasingFunction=ease});
  }
  void AfterPoof(Action done){var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(170)};timer.Tick+=delegate{timer.Stop();if(done!=null)Dispatcher.BeginInvoke(DispatcherPriority.Background,done);};timer.Start();}
  System.Windows.Controls.Image DroidImage(string key, double size) {
    var file = Path.Combine(Root, "assets", key + ".png"); if (!File.Exists(file)) return null;
    try { BitmapImage bmp; if(!ImageCache.TryGetValue(file,out bmp)){bmp=new BitmapImage();bmp.BeginInit();bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.UriSource=new Uri(file,UriKind.Absolute);bmp.EndInit();bmp.Freeze();ImageCache[file]=bmp;} return new System.Windows.Controls.Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.Uniform }; } catch { return null; }
  }
  void RememberUndo(Dictionary<string,object> member,string description){if(member==null)return;var checks=member.ContainsKey("checks")?D(member["checks"]):null;UndoHistory.Add(checks==null?new Dictionary<string,object>():new Dictionary<string,object>(checks));UndoMemberIds.Add(S(member,"id"));UndoDescriptions.Add(description??"change");while(UndoHistory.Count>MaxUndoHistory){UndoHistory.RemoveAt(0);UndoMemberIds.RemoveAt(0);UndoDescriptions.RemoveAt(0);}}
  void UndoLastChange(){if(UndoHistory.Count==0){if(Status!=null){Status.Visibility=Visibility.Visible;Status.Text="NOTHING TO UNDO";}return;}var index=UndoHistory.Count-1;var member=CurrentMember();if(member==null||S(member,"id")!=UndoMemberIds[index]){if(Status!=null){Status.Visibility=Visibility.Visible;Status.Text="UNDO UNAVAILABLE FOR THIS USER";}return;}var snapshot=UndoHistory[index];var label=UndoDescriptions[index];UndoHistory.RemoveAt(index);UndoMemberIds.RemoveAt(index);UndoDescriptions.RemoveAt(index);member["checks"]=new Dictionary<string,object>(snapshot);Page=0;LastRefresh=DateTime.UtcNow;if(Status!=null){Status.Visibility=Visibility.Visible;Status.Text="UNDID "+label.ToUpperInvariant()+"  |  "+UndoHistory.Count+" MORE";}Render();QueueRebirthSave(member);}
  int TierRank(string tier){var ranks=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"Default",1},{"Gold",2},{"Diamond",3},{"Rainbow",4},{"Beskar",5},{"Galactic",6}};return ranks.ContainsKey(tier??"")?ranks[tier]:0;}
  void MarkComplete(Dictionary<string, object> member, Dictionary<string, object> need, Button hit) {
    if(User==null)return;var canonical=S(need,"CheckKey");if(String.IsNullOrWhiteSpace(canonical))return;var checks=member.ContainsKey("checks")?D(member["checks"]):null;if(checks==null){checks=new Dictionary<string,object>();member["checks"]=checks;}var beforeChecks=new Dictionary<string,object>(checks);RememberUndo(member,"droid change");var wasComplete=B(need,"Complete");var keys=need.ContainsKey("MatchedKeys")?L(need["MatchedKeys"]).Select(Convert.ToString).Where(x=>!String.IsNullOrWhiteSpace(x)).ToList():new List<string>();if(keys.Count==0)keys.Add(canonical);var poofTargets=new List<FrameworkElement>{hit};
    if(!wasComplete){var selectedRank=TierRank(S(need,"Tier"));foreach(var lower in SearchNeeds(member).Where(x=>Norm(S(x,"Name"))==Norm(S(need,"Name"))&&TierRank(S(x,"Tier"))<=selectedRank)){var lowerCanonical=S(lower,"CheckKey");if(!String.IsNullOrWhiteSpace(lowerCanonical)){checks[lowerCanonical]=true;FrameworkElement lowerVisual;if(VisibleNeedTiles.TryGetValue(S(member,"id")+"|"+lowerCanonical,out lowerVisual)&&lowerVisual!=null&&!poofTargets.Contains(lowerVisual))poofTargets.Add(lowerVisual);}foreach(var matched in L(lower.ContainsKey("MatchedKeys")?lower["MatchedKeys"]:null).Select(Convert.ToString).Where(x=>!String.IsNullOrWhiteSpace(x)))checks[matched]=true;}foreach(var key in keys)checks[key]=true;}else foreach(var key in keys)checks[key]=false;
    var changedKeys=new Dictionary<string,object>();foreach(var key in beforeChecks.Keys.Union(checks.Keys)){var before=beforeChecks.ContainsKey(key)&&Convert.ToBoolean(beforeChecks[key]);var after=checks.ContainsKey(key)&&Convert.ToBoolean(checks[key]);if(before!=after)changedKeys[key]=after;}need["Complete"]=!wasComplete;hit.IsEnabled=false;QueueRebirthSave(member,changedKeys);if(!wasComplete){StartPoof(hit);AfterPoof(delegate{Render();});}else Render();
  }

  void QueueRebirthSave(Dictionary<string,object> member,Dictionary<string,object> changes=null) {
    PendingSaveMember=member;if(changes==null)PendingSaveFullSnapshot=true;else foreach(var pair in changes)PendingSaveChanges[pair.Key]=pair.Value;RebirthSavePending=true;RebirthSaveGeneration++;if(SaveDebounce==null){SaveDebounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};SaveDebounce.Tick+=delegate{SaveDebounce.Stop();FlushRebirthSave();};}SaveDebounce.Interval=TimeSpan.FromMilliseconds(300);SaveDebounce.Stop();SaveDebounce.Start();
  }
  void FlushRebirthSave() {
    if(RebirthSaveInFlight)return;var member=PendingSaveMember;if(member==null||User==null)return;RebirthSaveInFlight=true;var generation=RebirthSaveGeneration;var checks=member.ContainsKey("checks")?D(member["checks"]):new Dictionary<string,object>();var payload=new Dictionary<string,object>{{"active",S(member,"active")},{"source","overlay"},{"clientUpdatedAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}};if(PendingSaveFullSnapshot)payload["checks"]=new Dictionary<string,object>(checks);else payload["changes"]=new Dictionary<string,object>(PendingSaveChanges);var uid=S(User,"id");
    Task.Factory.StartNew(delegate{return Request("/users/"+Uri.EscapeDataString(uid)+"/rebirth","POST",payload);}).ContinueWith(t=>Dispatcher.Invoke(delegate{RebirthSaveInFlight=false;if(t.IsFaulted){Status.Text="SAVE RETRYING...";SaveDebounce.Interval=TimeSpan.FromSeconds(2);SaveDebounce.Start();}else{var response=D(t.Result);Status.Text="SAVED";SaveDebounce.Interval=TimeSpan.FromMilliseconds(300);LastRefresh=DateTime.UtcNow;if(response!=null&&B(response,"ignored")){RebirthSavePending=false;PendingSaveMember=null;PendingSaveChanges.Clear();PendingSaveFullSnapshot=false;LastRefresh=DateTime.MinValue;RefreshData();}else if(generation==RebirthSaveGeneration){var authoritative=response!=null&&response.ContainsKey("checks")?D(response["checks"]):null;if(authoritative!=null){member["checks"]=new Dictionary<string,object>(authoritative);if(!String.IsNullOrWhiteSpace(S(response,"active")))member["active"]=S(response,"active");Render();}RebirthSavePending=false;PendingSaveMember=null;PendingSaveChanges.Clear();PendingSaveFullSnapshot=false;}else FlushRebirthSave();}}));
  }

  string Norm(string value) { return new string((value ?? "").Where(Char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); }
  List<string> MatchingCheckKeys(Dictionary<string, object> checks, string cycle, string row, string name, string tier) {
    var matches=new List<string>();foreach(var pair in checks){bool set=false;Boolean.TryParse(Convert.ToString(pair.Value),out set);if(!set)continue;var parts=pair.Key.Split(':');if(parts.Length<4)continue;var keyCycle=parts[0];var keyRow=parts[1];var keyTier=parts[parts.Length-1];var keyName=String.Join(":",parts.Skip(2).Take(parts.Length-3));if(Norm(keyCycle)==Norm(cycle)&&Norm(keyRow)==Norm(row)&&Norm(keyName)==Norm(name)&&Norm(keyTier)==Norm(tier))matches.Add(pair.Key);}return matches;
  }
  bool CheckIsComplete(Dictionary<string, object> checks, string cycle, string row, string name, string tier) { return MatchingCheckKeys(checks,cycle,row,name,tier).Count>0; }

  bool RowDroidsComplete(Dictionary<string,object> row, Dictionary<string,object> checks, string cycle) {
    foreach(var req in L(row.ContainsKey("requirements")?row["requirements"]:null).Select(D).Where(x=>x!=null))foreach(var droid in L(req.ContainsKey("droids")?req["droids"]:null))if(!CheckIsComplete(checks,cycle,S(row,"rb"),Convert.ToString(droid),S(req,"tier")))return false; return true;
  }
  List<Dictionary<string, object>> Needs(Dictionary<string, object> member, int max) {
    var output=new List<Dictionary<string,object>>();var cycle=S(member,"active");var checks=member.ContainsKey("checks")?D(member["checks"]):null;if(checks==null)checks=new Dictionary<string,object>();var catalogCycle=Catalog.Select(D).FirstOrDefault(x=>x!=null&&S(x,"id")==cycle);if(catalogCycle==null||!catalogCycle.ContainsKey("rows"))return output;var rows=L(catalogCycle["rows"]).Select(D).Where(x=>x!=null).ToList();
    for(int ri=0;ri<rows.Count;ri++){var row=rows[ri];bool rowDone=RowDroidsComplete(row,checks,cycle);bool priorDone=rows.Take(ri).All(x=>RowDroidsComplete(x,checks,cycle));foreach(var req in L(row.ContainsKey("requirements")?row["requirements"]:null).Select(D).Where(x=>x!=null)){foreach(var droid in L(req.ContainsKey("droids")?req["droids"]:null)){var name=Convert.ToString(droid);var matchedKeys=MatchingCheckKeys(checks,cycle,S(row,"rb"),name,S(req,"tier"));bool complete=matchedKeys.Count>0;if(complete&&!(ShowCompleted&&Mode=="mine"))continue;bool future=rows.Skip(ri+1).Any(x=>L(x.ContainsKey("requirements")?x["requirements"]:null).Select(D).Where(q=>q!=null).Any(q=>L(q.ContainsKey("droids")?q["droids"]:null).Any(z=>Norm(Convert.ToString(z))==Norm(name))));var key=cycle+":"+S(row,"rb")+":"+name+":"+S(req,"tier");output.Add(new Dictionary<string,object>{{"Name",name},{"Tier",S(req,"tier")},{"RB",S(row,"rb")},{"NovaCrystals",row.ContainsKey("novaCrystals")?row["novaCrystals"]:null},{"Multiplier",S(row,"multiplier")},{"SrbMultiplier",S(row,"srbMultiplier")},{"CheckKey",key},{"MatchedKeys",matchedKeys.Cast<object>().ToList()},{"Complete",complete},{"Sell",complete&&rowDone&&priorDone&&!future}});}}}
    var rank=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"Default",1},{"Gold",2},{"Diamond",3},{"Rainbow",4},{"Beskar",5},{"Galactic",6}};foreach(var sellItem in output.Where(x=>B(x,"Complete")&&B(x,"Sell")).ToList()){int sellRank=rank.ContainsKey(S(sellItem,"Tier"))?rank[S(sellItem,"Tier")]:0;foreach(var lower in output.Where(x=>B(x,"Complete")&&Norm(S(x,"Name"))==Norm(S(sellItem,"Name")))){int lowerRank=rank.ContainsKey(S(lower,"Tier"))?rank[S(lower,"Tier")]:0;if(lowerRank<=sellRank)lower["Sell"]=true;}}
    return output.Take(max).ToList();
  }

  Brush TierBrush(string tier) { if(tier=="Beskar"){var m=new LinearGradientBrush();m.StartPoint=new Point(0,.5);m.EndPoint=new Point(1,.5);m.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F8FAFC"),0));m.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#94A3B8"),.35));m.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFFF"),.58));m.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#A8B4C4"),1));return m;} if (tier == "Rainbow") { var g = new LinearGradientBrush(); g.StartPoint = new Point(0, .5); g.EndPoint = new Point(1, .5); g.GradientStops.Add(new GradientStop(Colors.Red,0)); g.GradientStops.Add(new GradientStop(Colors.Orange,.2)); g.GradientStops.Add(new GradientStop(Colors.Yellow,.4)); g.GradientStops.Add(new GradientStop(Colors.LimeGreen,.6)); g.GradientStops.Add(new GradientStop(Colors.DeepSkyBlue,.8)); g.GradientStops.Add(new GradientStop(Colors.Violet,1)); return g; } return new SolidColorBrush(TierColorValue(tier)); }
  Color TierColorValue(string tier) { if (tier == "Galactic") return Color.FromRgb(168, 85, 247); if (tier == "Rainbow") return Color.FromRgb(34, 211, 238); if (tier == "Beskar") return Color.FromRgb(255, 138, 61); if (tier == "Diamond") return Color.FromRgb(96, 231, 255); if (tier == "Gold") return Color.FromRgb(244, 197, 66); return Color.FromRgb(154, 166, 186); }
  string Key(string s) { return new string((s ?? "").Where(Char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); }

  void SetOverlayLogicalHeight(double value){LogicalOverlayHeight=value;if(RootGrid!=null)RootGrid.Height=value;Width=560*OverlayScale;Height=value*OverlayScale;if(TimerHud!=null){TimerHud.Width=620*OverlayScale;TimerHud.Height=72*OverlayScale;}}

  IntPtr FindFortniteWindow(){
    if(CachedFortniteWindow!=IntPtr.Zero&&IsWindow(CachedFortniteWindow))return CachedFortniteWindow;var pids=new HashSet<uint>();try{foreach(var p in Process.GetProcessesByName("FortniteClient-Win64-Shipping"))pids.Add((uint)p.Id);}catch{}if(pids.Count==0)return IntPtr.Zero;
    IntPtr best=IntPtr.Zero;long bestArea=0;EnumWindows(delegate(IntPtr h,IntPtr unused){uint pid=0;GetWindowThreadProcessId(h,out pid);if(!pids.Contains(pid)||!IsWindowVisible(h))return true;RECT r;if(!GetWindowRect(h,out r))return true;long area=(long)Math.Max(0,r.Right-r.Left)*Math.Max(0,r.Bottom-r.Top);if(area>bestArea){bestArea=area;best=h;}return true;},IntPtr.Zero);CachedFortniteWindow=best;return best;
  }
  bool GetFortniteRect(IntPtr h,out RECT result){result=new RECT();RECT client;var origin=new NATIVEPOINT();if(GetClientRect(h,out client)&&client.Right-client.Left>1&&client.Bottom-client.Top>1&&ClientToScreen(h,ref origin)){result.Left=origin.X;result.Top=origin.Y;result.Right=origin.X+(client.Right-client.Left);result.Bottom=origin.Y+(client.Bottom-client.Top);return true;}return GetWindowRect(h,out result);}
  void PlaceWindowPhysical(Window window,int x,int y){var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;RECT current;if(GetWindowRect(handle,out current)&&Math.Abs(current.Left-x)<=2&&Math.Abs(current.Top-y)<=2)return;SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x0001|0x0004|0x0010);}

  void AnchorToFortnite() {
    if (User == null) return;var gameWindow=FindFortniteWindow();bool gameUnavailable=gameWindow==IntPtr.Zero;
    if(!gameUnavailable){var placement=new WINDOWPLACEMENT{length=Marshal.SizeOf(typeof(WINDOWPLACEMENT))};GetWindowPlacement(gameWindow,ref placement);int cloaked=0;try{DwmGetWindowAttribute(gameWindow,14,out cloaked,sizeof(int));}catch{}RECT visibilityRect;var hasRect=GetFortniteRect(gameWindow,out visibilityRect);var outsideVirtualScreen=hasRect&&(visibilityRect.Right<=SystemParameters.VirtualScreenLeft||visibilityRect.Left>=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth||visibilityRect.Bottom<=SystemParameters.VirtualScreenTop||visibilityRect.Top>=SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight);gameUnavailable=IsIconic(gameWindow)||placement.showCmd==2||!IsWindowVisible(gameWindow)||cloaked!=0||!hasRect||outsideVirtualScreen||(visibilityRect.Right-visibilityRect.Left)<=1||(visibilityRect.Bottom-visibilityRect.Top)<=1;}
    if (gameUnavailable) { FortniteMissingTicks++;if(FortniteMissingTicks>=3){if(IsVisible) Hide(); if(TimerHud!=null&&TimerHud.IsVisible)TimerHud.Hide();}return; }
    FortniteMissingTicks=0;
    if(!IsVisible) Show(); if(TimerHud!=null&&!TimerHud.IsVisible)TimerHud.Show(); RECT r;
    if (GetFortniteRect(gameWindow, out r)) {
      var gameHeight=r.Bottom-r.Top;var gameWidth=r.Right-r.Left;var desiredScale=Math.Max(.72,Math.Min(1.10,Math.Min(gameWidth/2560.0,gameHeight/1440.0)));if(Math.Abs(desiredScale-OverlayScale)>.01){OverlayScale=desiredScale;SetOverlayLogicalHeight(LogicalOverlayHeight);}
      RECT overlayRect;var overlayHandle=new WindowInteropHelper(this).Handle;GetWindowRect(overlayHandle,out overlayRect);var overlayWidth=Math.Max(1,overlayRect.Right-overlayRect.Left);var overlayHeight=Math.Max(1,overlayRect.Bottom-overlayRect.Top);if(Double.IsNaN(OverlayTopRatio))OverlayTopRatio=Math.Max(0,((gameHeight-overlayHeight)/2.0)/Math.Max(1,gameHeight));var x=r.Right-overlayWidth-(int)Math.Round(24*OverlayScale);var y=r.Top+(int)Math.Round(OverlayTopRatio*gameHeight);y=Math.Max(r.Top+12,Math.Min(y,r.Bottom-overlayHeight-12));PlaceWindowPhysical(this,x,y);
      if(TimerHud!=null){RECT timerRect;var timerHandle=new WindowInteropHelper(TimerHud).Handle;GetWindowRect(timerHandle,out timerRect);var timerWidth=Math.Max(1,timerRect.Right-timerRect.Left);PlaceWindowPhysical(TimerHud,r.Left+(gameWidth-timerWidth)/2,r.Top+18);}
    }
  }
  void CenterLoginFallback() { var a = SystemParameters.WorkArea; Left = a.Right - Width - 24; Top = a.Top + Math.Max(18, (a.Height - Height) / 2); }
  void UpdateTimers() {
    if (MythicTimerText == null || GalacticTimerText == null || BeskarTimerText == null) return; var now = DateTimeOffset.UtcNow+ServerClockOffset; var mythic = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 55, 0, TimeSpan.Zero); if (mythic <= now) mythic = mythic.AddHours(1); var galactic = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 45, 0, TimeSpan.Zero); if (galactic <= now) galactic = galactic.AddHours(1);
    var ml = (int)(mythic - now).TotalSeconds; var gl = (int)(galactic - now).TotalSeconds; var sec = now.ToUnixTimeSeconds(); var bl = 900 - (sec % 900);
    MythicTimerText.Text = String.Format("{0:00}:{1:00}", ml / 60, ml % 60); GalacticTimerText.Text = String.Format("{0:00}:{1:00}", gl / 60, gl % 60); BeskarTimerText.Text = String.Format("{0:00}:{1:00}", bl / 60, bl % 60); if(MythicFill!=null)MythicFill.Width=192*Math.Max(0,Math.Min(1,1-(ml/3600.0))); if(GalacticFill!=null)GalacticFill.Width=192*Math.Max(0,Math.Min(1,1-(gl/3600.0))); if(BeskarFill!=null)BeskarFill.Width=192*Math.Max(0,Math.Min(1,1-(bl/900.0)));
  }
  [STAThread] public static void Main() { if(!ValidateLauncherGate()){MessageBox.Show("Please start DroidTrakr from the DroidTrakr Launcher.","DroidTrakr Launcher Required",MessageBoxButton.OK,MessageBoxImage.Information);return;} try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay-launch.log"), ""); try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { try { SetProcessDPIAware(); } catch { } } ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; Log("WPF entry point"); var app = new Application(); app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e) { Log("UNHANDLED: " + e.Exception); }; app.Run(new DroidTrakrOverlay()); Log("WPF application exited"); } catch (Exception ex) { Log("FATAL: " + ex); MessageBox.Show(ex.ToString(), "DroidTrakr Overlay startup error"); } }
}
