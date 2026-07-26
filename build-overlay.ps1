$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'DroidTrakr Fortnite Overlay.exe'
$icon=Join-Path $root 'DroidTrakr.ico'
$log=Join-Path $root 'build-overlay.log'
try {
  Get-Process | Where-Object { $_.Path -eq $out } | Stop-Process -Force -ErrorAction SilentlyContinue
  if(Test-Path $out){Remove-Item $out -Force}
  Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Web.Extensions,System.Windows.Forms,System.Drawing,System.Security
  $cp=New-Object System.CodeDom.Compiler.CompilerParameters
  $cp.GenerateExecutable=$true
  $cp.GenerateInMemory=$false
  $cp.OutputAssembly=$out
  $cp.CompilerOptions='/target:winexe /win32icon:"'+$icon+'"'
  @([System.Windows.Window].Assembly.Location,[System.Windows.Media.Brush].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,[System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,[System.Linq.Enumerable].Assembly.Location,[System.Net.WebClient].Assembly.Location,[System.Windows.Forms.NotifyIcon].Assembly.Location,[System.Drawing.Icon].Assembly.Location,[System.Security.Cryptography.ProtectedData].Assembly.Location) | Select-Object -Unique | ForEach-Object {[void]$cp.ReferencedAssemblies.Add($_)}
  Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $root 'DroidTrakrOverlay.cs')) -CompilerParameters $cp
  $f=Get-Item $out
  $result=("BUILT WPF WindowsApplication WITH ICON: {0} ({1} bytes)" -f $f.FullName,$f.Length)
  $result | Set-Content -Encoding UTF8 $log
  Write-Output $result
} catch {
  ($_ | Out-String) | Set-Content -Encoding UTF8 $log
  throw
}
