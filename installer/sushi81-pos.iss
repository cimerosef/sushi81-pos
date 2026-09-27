#define ProductVersion GetEnv("SUSHI81_PRODUCT_VERSION")
#define ProductFileVersion GetEnv("SUSHI81_FILE_VERSION")
#define SourceShort GetEnv("SUSHI81_SOURCE_SHORT")
#define PublishDir GetEnv("SUSHI81_PUBLISH_DIR")
#define PackageDir GetEnv("SUSHI81_PACKAGE_OUT_DIR")
#define DeploymentProfile GetEnv("SUSHI81_PROFILE")
#define ProfileAppId GetEnv("SUSHI81_APP_ID")
#define ProfileAppName GetEnv("SUSHI81_APP_NAME")
#define ProfileInstallDirectory GetEnv("SUSHI81_INSTALL_DIRECTORY")
#define ProfileOutputBaseName GetEnv("SUSHI81_OUTPUT_BASE_NAME")

; Inno Setup's ISCC.exe has no useful Windows file-version resource. Ver is
; the compiler engine version, encoded as major/minor/revision/build bytes.
#if Ver != (6 * 16777216 + 7 * 65536 + 3 * 256)
  #error This installer must be compiled with Inno Setup 6.7.3.
#endif

#if ProductVersion == ""
  #error SUSHI81_PRODUCT_VERSION is required.
#endif
#if SourceShort == ""
  #error SUSHI81_SOURCE_SHORT is required.
#endif
#if PublishDir == ""
  #error SUSHI81_PUBLISH_DIR is required.
#endif
#if PackageDir == ""
  #error SUSHI81_PACKAGE_OUT_DIR is required.
#endif
#if DeploymentProfile != "prod" && DeploymentProfile != "preprod"
  #error SUSHI81_PROFILE must be exactly prod or preprod.
#endif
#if ProfileAppId == ""
  #error SUSHI81_APP_ID is required.
#endif
#if ProfileAppName == ""
  #error SUSHI81_APP_NAME is required.
#endif
#if ProfileInstallDirectory == ""
  #error SUSHI81_INSTALL_DIRECTORY is required.
#endif
#if ProfileOutputBaseName == ""
  #error SUSHI81_OUTPUT_BASE_NAME is required.
#endif
[Setup]
AppId={{#ProfileAppId}}
AppName={#ProfileAppName}
AppVersion={#ProductVersion}
AppVerName={#ProfileAppName} {#ProductVersion}
AppPublisher=Sushi81
AppPublisherURL=https://github.com/cimerosef/sushi81-pos
DefaultDirName={localappdata}\Programs\{#ProfileInstallDirectory}
DefaultGroupName={#ProfileAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName={#ProfileAppName}
OutputDir={#PackageDir}
OutputBaseFilename={#ProfileOutputBaseName}
SetupLogging=yes
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#ProductFileVersion}
VersionInfoCompany=Sushi81
VersionInfoDescription=Sushi81 POS per-user {#DeploymentProfile} installer
VersionInfoProductName={#ProfileAppName}
VersionInfoProductVersion={#ProductVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProfileAppName}"; Filename: "{app}\Sushi81.Pos.Desktop.exe"; WorkingDir: "{app}"
