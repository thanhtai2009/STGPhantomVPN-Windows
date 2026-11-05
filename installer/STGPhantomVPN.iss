; STG Phantom VPN - Inno Setup 6 script. Built by build/build-installer.sh; see README.md.
;
;   iscc /DAppDir=<staged app folder> /DOutputDir=<folder> STGPhantomVPN.iss
;
; AppDir is the same folder that goes into the portable zip (exe, DLLs, satellite folders, Assets, licenses).

#ifndef AppVersion
  #define AppVersion "1.0.11"
#endif
#ifndef AppDir
  #error Define AppDir: the staged application folder.
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

#define AppName "STG Phantom VPN"
#define AppExe "STGPhantomVPN.exe"
#define AppPublisher "PNC STG"
#define NetFxUrl "https://dotnet.microsoft.com/download/dotnet-framework/net48"

[Setup]
; Fixed for the life of the product; must match InstallInfo.AppId in Core/HelperPlacement.cs.
AppId={{8B1BA3B9-35C6-497C-9711-427B7DE8A5CB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Designed by TaiNT13@PNC STG
AppComments=VPNGate OpenVPN client
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoCopyright=Designed by TaiNT13@PNC STG
; Program Files only: the elevated helper must run from a folder that unelevated programs cannot change.
PrivilegesRequired=admin
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Windows 7 SP1 and later. Windows 8 (6.2) is stopped by the .NET Framework 4.8 check below.
MinVersion=6.1sp1
; AnyCPU exe (Prefer32Bit=false) runs on x86, x64 and ARM64, so every architecture is allowed. 64-bit
; Windows (x64 and ARM64, including Windows 10 on ARM without x64 emulation) installs to the 64-bit
; Program Files; 32-bit Windows installs to its only Program Files.
ArchitecturesInstallIn64BitMode=x64compatible or arm64
WizardStyle=modern
SetupIconFile=..\src\STGPhantomVPN\Assets\app.ico
; Brand panels from assets/brand/build_brand.py; Inno Setup picks the variant closest to the display scale.
WizardImageFile=wizard-image-100.bmp,wizard-image-125.bmp,wizard-image-150.bmp,wizard-image-175.bmp,wizard-image-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-125.bmp,wizard-small-150.bmp,wizard-small-175.bmp,wizard-small-200.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
; Single-instance mutex from App.xaml.cs (InstallInfo.SingleInstanceMutex).
AppMutex=Global\STGPhantomVPN.SingleInstance
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto
SetupLogging=yes
OutputDir={#OutputDir}
OutputBaseFilename=STGPhantomVPN-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Single-threaded, in-process compression and UTC file times keep the output byte-for-byte reproducible.
LZMAUseSeparateProcess=no
LZMANumBlockThreads=1
TimeStampsInUTC=yes

[Languages]
; English first: it is used when the Windows UI language has no translation (for example Khmer, which
; has no Inno Setup translation yet). Vietnamese and Chinese Simplified are the unofficial translations
; from jrsoftware.org/files/istrans, pinned to the Inno Setup 6.7.4 tag (see build/build-installer.sh).
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "vietnamese"; MessagesFile: "Languages\Vietnamese.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
english.NetFxMissing={#AppName} needs Microsoft .NET Framework 4.8, which is not installed on this computer.%n%nDownload it from Microsoft:%n%1%n%nOpen the download page now? Setup will exit; run it again after installing .NET Framework 4.8.
vietnamese.NetFxMissing={#AppName} cần Microsoft .NET Framework 4.8 nhưng máy này chưa cài.%n%nTải từ trang chính thức của Microsoft:%n%1%n%nMở trang tải ngay? Trình cài đặt sẽ thoát; hãy chạy lại sau khi cài .NET Framework 4.8.
chinesesimplified.NetFxMissing={#AppName} 需要 Microsoft .NET Framework 4.8，但此计算机尚未安装。%n%n请从 Microsoft 官方网站下载：%n%1%n%n现在打开下载页面吗？安装程序将退出；安装 .NET Framework 4.8 后请重新运行。
english.Win7Note=Windows 7 SP1: on first start the app checks for the required updates (KB2999226, KB4474419, KB4019990) and shows Microsoft Update Catalog links for any that are missing.
vietnamese.Win7Note=Windows 7 SP1: khi chạy lần đầu, ứng dụng sẽ kiểm tra các bản cập nhật cần thiết (KB2999226, KB4474419, KB4019990) và hiện liên kết Microsoft Update Catalog cho bản còn thiếu.
chinesesimplified.Win7Note=Windows 7 SP1：应用首次启动时会检查所需更新（KB2999226、KB4474419、KB4019990），并为缺少的更新显示 Microsoft Update Catalog 链接。
english.UninstallRemoveData=Also delete the app data in %1 (logs and downloaded installers)?%n%nChoose No to keep it for a later reinstall. Your personal settings in %2 are always kept. OpenVPN and WireGuard (if installed) are not removed.
vietnamese.UninstallRemoveData=Xóa luôn dữ liệu ứng dụng trong %1 (nhật ký, bộ cài đã tải)?%n%nChọn Không để giữ lại cho lần cài sau. Cài đặt cá nhân trong %2 luôn được giữ. OpenVPN và WireGuard (nếu có) không bị gỡ.
chinesesimplified.UninstallRemoveData=是否同时删除 %1 中的应用数据（日志、已下载的安装程序）？%n%n选择“否”可保留以便日后重新安装。%2 中的个人设置始终保留。OpenVPN 和 WireGuard（如已安装）不会被卸载。
english.UninstallCleanupFailed=Some items could not be removed (exit code %1). Details are in %2.
vietnamese.UninstallCleanupFailed=Không gỡ được một số mục (mã thoát %1). Chi tiết trong %2.
chinesesimplified.UninstallCleanupFailed=部分项目无法删除（退出代码 %1）。详情见 %2。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Bundled OpenVPN MSIs and GPL sources (build.sh --bundle-openvpn) stay in the zip only; the app downloads
; the pinned OpenVPN installer itself.
Source: "{#AppDir}\*"; DestDir: "{app}"; Excludes: "\redist,\source"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 1.0.10 dropped Material Design In XAML Toolkit (own Fluent theme); remove the old DLLs and their licences on upgrade.
Type: files; Name: "{app}\MaterialDesignThemes.Wpf.dll"
Type: files; Name: "{app}\MaterialDesignColors.dll"
Type: files; Name: "{app}\Microsoft.Xaml.Behaviors.dll"
Type: files; Name: "{app}\licenses\MaterialDesignInXamlToolkit-LICENSE.txt"
Type: files; Name: "{app}\licenses\Microsoft.Xaml.Behaviors-LICENSE.txt"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The app is asInvoker and must run as the signed-in user, not with Setup's elevated token.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  NetFx48Release = 528040;

function IsNetFx48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= NetFx48Release);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := IsNetFx48Installed;
  if not Result then
  begin
    Log('.NET Framework 4.8 (Release >= 528040) not found');
    if SuppressibleMsgBox(FmtMessage(CustomMessage('NetFxMissing'), ['{#NetFxUrl}']), mbCriticalError, MB_YESNO, IDNO) = IDYES then
      ShellExecAsOriginalUser('open', '{#NetFxUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

function IsWindows7: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 6) and (Version.Minor = 1);
end;

procedure AddMemo(var Memo: String; const Part, NewLine: String);
begin
  if Part <> '' then
  begin
    if Memo <> '' then
      Memo := Memo + NewLine + NewLine;
    Memo := Memo + Part;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  AddMemo(Result, MemoDirInfo, NewLine);
  AddMemo(Result, MemoGroupInfo, NewLine);
  AddMemo(Result, MemoTasksInfo, NewLine);
  if IsWindows7 then
    AddMemo(Result, CustomMessage('Win7Note'), NewLine);
end;

{ Uninstall clean-up runs through the installed app's elevated helper (STGPhantomVPN.exe --elevated ...),
  which deletes the app's Administrators-only data folder without following links. OpenVPN and WireGuard
  are never uninstalled. }

function DataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\STGPhantomVPN');
end;

procedure RunHelper(const Verb: String);
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\{#AppExe}'), '--elevated ' + Verb, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Log('Helper ' + Verb + ' exit code ' + IntToStr(ResultCode));
  if ResultCode <> 0 then
    SuppressibleMsgBox(FmtMessage(CustomMessage('UninstallCleanupFailed'), [IntToStr(ResultCode), DataDir + '\logs']), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  if DirExists(DataDir) and
     (SuppressibleMsgBox(FmtMessage(CustomMessage('UninstallRemoveData'), [DataDir, '%LOCALAPPDATA%\STGPhantomVPN']),
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES) then
    RunHelper('remove-data');
end;
