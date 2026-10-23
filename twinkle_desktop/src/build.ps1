$ErrorActionPreference = 'Stop'
$twinkleBuildRoot = Split-Path -Parent $PSScriptRoot
$twinkleFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$twinkleReferences = @('WPF\PresentationFramework.dll','WPF\PresentationCore.dll','WPF\WindowsBase.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Net.Http.dll','System.Security.dll') | ForEach-Object { '/reference:' + (Join-Path $twinkleFramework $_) }
$twinkleMailReferences = @('MailKit.dll','MimeKit.dll','System.Threading.Tasks.Extensions.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll') | ForEach-Object { '/reference:' + (Join-Path $twinkleBuildRoot $_) }
& (Join-Path $twinkleFramework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/out:' + (Join-Path $twinkleBuildRoot 'TwinkleDesktop.exe')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/win32icon:' + (Join-Path $twinkleBuildRoot 'assets\twinkle.ico')) @twinkleReferences @twinkleMailReferences (Join-Path $PSScriptRoot 'TwinkleDesktop.cs') (Join-Path $PSScriptRoot 'ImageLibrary.cs') (Join-Path $PSScriptRoot 'Notifications.cs') (Join-Path $PSScriptRoot 'MailCore.cs') (Join-Path $PSScriptRoot 'MailWindow.cs') (Join-Path $PSScriptRoot 'MailRegression.cs') (Join-Path $PSScriptRoot 'Distribution.cs')
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed' }
& (Join-Path $twinkleFramework 'csc.exe') /nologo /target:exe /platform:anycpu /optimize+ /utf8output ('/out:' + (Join-Path $twinkleBuildRoot 'TwinkleNativeHost.exe')) ('/reference:' + (Join-Path $twinkleFramework 'System.Web.Extensions.dll')) (Join-Path $PSScriptRoot 'Notifications.cs') (Join-Path $PSScriptRoot 'NativeHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native messaging host compilation failed' }

