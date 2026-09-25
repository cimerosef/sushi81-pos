#define ProductVersion GetEnv("SUSHI81_PRODUCT_VERSION")
#define ProductFileVersion GetEnv("SUSHI81_FILE_VERSION")
#define SourceShort GetEnv("SUSHI81_SOURCE_SHORT")
#define PublishDir GetEnv("SUSHI81_PUBLISH_DIR")
#define PackageDir GetEnv("SUSHI81_PACKAGE_OUT_DIR")

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

[Setup]
AppId={{C7A1B9E2-1E62-4B4B-A2EA-7802814408FC}
AppName=Sushi81 POS
AppVersion={#ProductVersion}
AppVerName=Sushi81 POS {#ProductVersion}
AppPublisher=Sushi81
AppPublisherURL=https://github.com/cimerosef/sushi81-pos
DefaultDirName={localappdata}\Programs\Sushi81 POS
DefaultGroupName=Sushi81 POS
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=Sushi81 POS
UninstallDisplayVersion={#ProductVersion}
OutputDir={#PackageDir}
OutputBaseFilename=Sushi81POS-Setup-{#ProductVersion}-{#SourceShort}
SetupLogging=yes
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#ProductFileVersion}
VersionInfoCompany=Sushi81
VersionInfoDescription=Sushi81 POS per-user installer
VersionInfoProductName=Sushi81 POS
VersionInfoProductVersion={#ProductVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sushi81 POS"; Filename: "{app}\Sushi81.Pos.Desktop.exe"; WorkingDir: "{app}"
