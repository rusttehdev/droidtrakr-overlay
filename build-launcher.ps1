$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'DroidTrakr Launcher.exe'
$icon=Join-Path $root 'DroidTrakr.ico'
Get-Process -Name 'DroidTrakr Launcher' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if(Test-Path $out){Remove-Item $out -Force}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
$cp=New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable=$true
$cp.GenerateInMemory=$false
$cp.OutputAssembly=$out
$cp.CompilerOptions='/target:winexe /win32icon:"'+$icon+'"'
@([System.Windows.Window].Assembly.Location,[System.Windows.Media.Brush].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,[System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,[System.Linq.Enumerable].Assembly.Location,[System.Net.WebClient].Assembly.Location,[System.IO.Compression.ZipArchive].Assembly.Location,[System.IO.Compression.ZipFile].Assembly.Location) | Select-Object -Unique | ForEach-Object {[void]$cp.ReferencedAssemblies.Add($_)}
Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $root 'DroidTrakrLauncher.cs')) -CompilerParameters $cp
$f=Get-Item $out
Write-Output ("BUILT LAUNCHER WITH ICON: {0} ({1} bytes)" -f $f.FullName,$f.Length)
