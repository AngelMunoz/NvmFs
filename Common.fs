namespace NvmFs

open System
open System.Runtime.InteropServices
open Thoth.Json.Net

type NodeVerItem =
    { version: string
      date: string
      files: string list
      npm: string option
      v8: string
      uv: string option
      zlib: string option
      openssl: string option
      modules: string option
      lts: string option
      security: bool }

    static member Decoder: Decoder<NodeVerItem> =

        let customDecode (value: string) (jvalue: JsonValue): Result<string option, DecoderError> =
            match Decode.bool value jvalue with
            | Ok _ -> Ok None
            | Error _ ->
                match Decode.string value jvalue with
                | Ok str -> Ok(Option.ofObj str)
                | Error err -> Error err

        Decode.object
            (fun get ->
                { version = get.Required.Field "version" Decode.string
                  date = get.Required.Field "date" Decode.string
                  files = get.Required.Field "files" (Decode.list Decode.string)
                  npm = get.Optional.Field "npm" Decode.string
                  v8 = get.Required.Field "v8" Decode.string
                  uv = get.Optional.Field "uv" Decode.string
                  zlib = get.Optional.Field "zlib" Decode.string
                  openssl = get.Optional.Field "openssl" Decode.string
                  modules = get.Optional.Field "modules" Decode.string
                  lts = get.Required.Field "lts" customDecode
                  security = get.Required.Field "security" Decode.bool })

type InstallType =
    | LTS
    | Current
    | SpecificM of string
    | SpecificMM of string * string
    | SpecificMMP of string * string * string

module Common =

    [<RequireQualifiedAccess>]
    module EnvVars =
        [<Literal>]
        let NvmFsHome = "NVMFS_HOME"

        [<Literal>]
        let NvmSourceBaseUrl = "NVM_SOURCE_BASE_URL"

    let getVersionCodename (version: string) = $"{version.Split('.').[0]}.x"

    let getLtsCodename (version: string) = $"{version.ToLowerInvariant()}"

    let getHome () =
        Environment.GetEnvironmentVariable(EnvVars.NvmFsHome)
        |> Option.ofObj
        |> Option.defaultValue
            (System.IO.Path.GetFullPath $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/nvmfs")

    let getSrcBaseUrl () =
        Environment.GetEnvironmentVariable(EnvVars.NvmSourceBaseUrl)
        |> Option.ofObj
        |> Option.defaultValue "https://nodejs.org/dist"

    let getPlatform () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        then "linux"
        else if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        then "win"
        else if RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        then "darwin"
        else ""

    let getArch () =
        match RuntimeInformation.OSArchitecture with
        | Architecture.Arm -> "armv7l"
        | Architecture.Arm64 -> "arm64"
        | Architecture.X64 -> "x64"
        | Architecture.X86 -> "x86"
        | _ -> ""


    let getVersionItem (versions: NodeVerItem []) (install: InstallType) =
        match install with
        | LTS ->
            versions
            |> Array.choose (fun version -> if version.lts.IsSome then Some version else None)
            |> Array.tryHead
        | Current -> versions |> Array.tryHead
        | SpecificM major ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.StartsWith($"{major}."))
        | SpecificMM (major, minor) ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.StartsWith($"{major}.{minor}."))
        | SpecificMMP (major, minor, patch) ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.Contains($"{major}.{minor}.{patch}"))
