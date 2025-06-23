namespace NvmFs

open System
open System.Runtime.InteropServices

open FsToolkit.ErrorHandling

open JDeck

type NodeVerItem = {
  version: string
  date: string
  files: string list
  npm: string option
  v8: string
  uv: string option
  zlib: string option
  openssl: string option
  modules: string option
  lts: string option
  security: bool
} with

  static member Decoder: Decoder<NodeVerItem> =
    fun nodeVersion ->

      let ltsDecoder lts =

        match Required.Property.get ("lts", Required.boolean) lts with
        | Ok _ -> Ok None
        | Error _ -> Required.Property.get ("lts", Optional.string) lts

      result {
        let! version =
          nodeVersion |> Required.Property.get("version", Required.string)

        let! date =
          nodeVersion |> Required.Property.get("date", Required.string)

        let! files =
          nodeVersion |> Required.Property.list("files", Required.string)

        let! npm = nodeVersion |> Optional.Property.get("npm", Required.string)

        let! v8 = nodeVersion |> Required.Property.get("v8", Required.string)
        let! uv = nodeVersion |> Optional.Property.get("uv", Required.string)

        let! zlib =
          nodeVersion |> Optional.Property.get("zlib", Required.string)

        let! openssl =
          nodeVersion |> Optional.Property.get("openssl", Required.string)

        let! modules =
          nodeVersion |> Optional.Property.get("modules", Required.string)

        let! lts = ltsDecoder nodeVersion

        let! security =
          nodeVersion |> Required.Property.get("security", Required.boolean)

        return {
          version = version
          date = date
          files = files
          npm = npm
          v8 = v8
          uv = uv
          zlib = zlib
          openssl = openssl
          modules = modules
          lts = lts
          security = security
        }
      }

type InstallType =
  | LTS
  | Current
  | SpecificM of string
  | SpecificMM of string * string
  | SpecificMMP of string * string * string

  member this.asString =
    match this with
    | LTS -> "lts"
    | Current -> "current"
    | SpecificM m -> m
    | SpecificMM(major, minor) -> $"{major}.{minor}"
    | SpecificMMP(major, minor, patch) -> $"{major}.{minor}.{patch}"

type CurrentOS =
  | Linux
  | Mac
  | Windows
  | FreeBSD

  member this.AsNodeUrlValue =
    match this with
    | Linux -> "linux"
    | Mac -> "darwin"
    | Windows -> "win"
    | FreeBSD -> "freebsd"

type SystemError =
  | FailedToGetOS
  | FailedToGetArch

type InstallError =
  | VersionNotFound of string
  | FailedToSetDefault of string
  | PlatformError

[<RequireQualifiedAccess>]
type UninstallError =
  | PlatformError
  | NodeNotInDisk
  | FailedToDelete of exn

type SetDefaultError =
  | SymlinkError of string
  | PermissionError of string
  | UnsuppoertdOS

  member this.Value =
    match this with
    | SymlinkError err -> err
    | PermissionError err -> err
    | UnsuppoertdOS -> "The OS is not supported"

[<RequireQualifiedAccess>]
type UseError =
  | NodeNotInDisk of string
  | FailedToSetDefault of string
  | PlatformError

module Common =
  open FsToolkit.ErrorHandling

  [<RequireQualifiedAccess>]
  module EnvVars =
    [<Literal>]
    let NvmFsHome = "NVMFS_HOME"

    [<Literal>]
    let NvmFsNode = "NVMFS_NODE"

    [<Literal>]
    let NvmSourceBaseUrl = "NVM_SOURCE_BASE_URL"

  [<Literal>]
  let StartMarker = "###   NVMFS  START   ###"

  [<Literal>]
  let EndMarker = "###   NVMFS  END    ###"

  let getVersionCodename(version: string) = $"{version.Split('.').[0]}.x"

  let getLtsCodename(version: string) = version.ToLowerInvariant()

  let getCodename(version: NodeVerItem) =
    let defVersion = getVersionCodename version.version

    let defLts = version.lts |> Option.map getLtsCodename

    defaultArg defLts (defVersion)

  let getHome() =
    Environment.GetEnvironmentVariable(EnvVars.NvmFsHome)
    |> Option.ofObj
    |> Option.defaultValue(
      System.IO.Path.GetFullPath
        $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}/nvmfs"
    )

  let getSrcBaseUrl() =
    Environment.GetEnvironmentVariable(EnvVars.NvmSourceBaseUrl)
    |> Option.ofObj
    |> Option.defaultValue "https://nodejs.org/dist"

  let getOSPlatform() =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
      Ok Linux
    else if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
      Ok Windows
    else if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
      Ok Mac
    else if RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) then
      Error "FreeBSD is not supported"
    else
      Error "Unrecognized platform"

  let (|IsMacOrLinux|IsWindows|Unsupported|) os =
    match os with
    | Ok Mac
    | Ok Linux -> IsMacOrLinux
    | Ok Windows -> IsWindows
    | Ok FreeBSD -> Unsupported "FreeBSD is not supported"
    | Error value -> Unsupported value

  let getArch() =
    match RuntimeInformation.OSArchitecture with
    | Architecture.Arm -> Ok "armv7l"
    | Architecture.Arm64 -> Ok "arm64"
    | Architecture.X64 -> Ok "x64"
    | Architecture.X86 -> Ok "x86"
    | Architecture.Wasm -> Error "Wasm is not supported"
    | value -> Error $"{value} is not supported"


  let getVersionItem (versions: NodeVerItem[]) (install: InstallType) =
    match install with
    | LTS ->
      versions
      |> Array.choose(fun version ->
        if version.lts.IsSome then Some version else None)
      |> Array.tryHead
    | Current -> versions |> Array.tryHead
    | SpecificM major ->
      let major =
        if major.ToLowerInvariant().StartsWith('v') then
          major
        else
          $"v{major}"

      versions |> Array.tryFind(fun ver -> ver.version.StartsWith($"{major}."))
    | SpecificMM(major, minor) ->
      let major =
        if major.ToLowerInvariant().StartsWith('v') then
          major
        else
          $"v{major}"

      versions
      |> Array.tryFind(fun ver -> ver.version.StartsWith($"{major}.{minor}."))
    | SpecificMMP(major, minor, patch) ->
      let major =
        if major.ToLowerInvariant().StartsWith('v') then
          major
        else
          $"v{major}"

      versions
      |> Array.tryFind(fun ver ->
        ver.version.Contains($"{major}.{minor}.{patch}"))

  let getVersionDirName (version: string) (os: CurrentOS) (arch: string) =
    $"node-%s{version}-%s{os.AsNodeUrlValue}-%s{arch}"

  let getOsArchCodename(version) = result {
    let codename = getCodename version

    let! os = getOSPlatform() |> Result.setError FailedToGetOS

    let! arch = getArch() |> Result.setError FailedToGetArch

    return codename, os, arch
  }
