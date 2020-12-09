// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Net.Http
open FSharp.Control.Tasks
open SharpCompress.Common
open SharpCompress.Readers
open CliWrap
open CliWrap.Buffered

let createSymlink (symbolicLink: string) (actualPath: string) =
    if (RuntimeInformation.IsOSPlatform OSPlatform.Linux || RuntimeInformation.IsOSPlatform OSPlatform.OSX) then
        let symbolicLinkDirectory = Path.GetDirectoryName(symbolicLink)
    
        if not (Directory.Exists(symbolicLinkDirectory)) then
            Directory.CreateDirectory(symbolicLinkDirectory) |> ignore

        let result =
            Cli
                .Wrap("ln")
                .WithArguments($"-s {actualPath} {symbolicLink}")
                .ExecuteBufferedAsync().Task |> Async.AwaitTask |> Async.RunSynchronously

        if result.ExitCode <> 0 then
            printfn "%s" result.StandardError
        ()
    else
        printfn $"Could not write symlink {symbolicLink} -> {actualPath}, for more information please see https://github.com/dotnet/runtime/issues/24271"

let extractContents (os: string) (source: string) (output: string) =
    match os with 
    | "win" ->
        ZipFile.ExtractToDirectory(source, output)
    | _ -> 
        let sourceFile = FileInfo(source)
        use sourceStr = sourceFile.OpenRead()
        use reader = ReaderFactory.Open(sourceStr)
        let opts = ExtractionOptions()
        opts.ExtractFullPath <- true
        opts.Overwrite <- true
        opts.WriteSymbolicLink <- new ExtractionOptions.SymbolicLinkWriterDelegate(createSymlink)
        reader.WriteAllToDirectory(output, opts)



let downloadChecksumsForVersion (codename: string) =
    task {
        let url = $"https://nodejs.org/dist/{codename}/SHASUMS256.txt"
        use http = new HttpClient()
        use! checksumsStr = http.GetStreamAsync(url)
        use checksums = File.OpenWrite(Path.GetFullPath($"./{codename}/SHASUMS256.txt"))
        do! checksumsStr.CopyToAsync(checksums)
        return checksums.Name
    }

let downloadNode (codename: string) (version: string) (os: string) (arch: string) =
    task {
        let extension = if os = "win" then ".zip" else ".tar.gz"
        let filename = $"node-{version}-{os}-{arch}{extension}"
        let url = $"https://nodejs.org/dist/{codename}/{filename}"
        printfn $"Downloading file: {url}"
        use http = new HttpClient()
        use! str = http.GetStreamAsync(url)
        use file = File.OpenWrite(Path.GetFullPath($"./{filename}"))
        do! str.CopyToAsync(file)
        return file.Name
    }

let verifyChecksum(file: string) (against: string) =
    use sha256 = SHA256.Create()
    use str = File.OpenRead(Path.GetFullPath(file))
    let hash = sha256.ComputeHash str
    let checksum = BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant()
    checksum = against

let getPlatform() = 
    if RuntimeInformation.IsOSPlatform(OSPlatform.Linux)  then "linux"
    else if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)  then "win"
    else if RuntimeInformation.IsOSPlatform(OSPlatform.OSX)  then "darwin"
    else ""

let getArch() =
    match RuntimeInformation.OSArchitecture with 
    | Architecture.Arm -> "armv7l"
    | Architecture.Arm64 -> "arm64"
    | Architecture.X64 -> "x64"
    | Architecture.X86 -> "x86"
    | _ -> ""
// Define a function to construct a message to print
let from whom =
    let os = getPlatform()
    let arch = getArch()
    sprintf "from %s-%s" os arch

[<EntryPoint>]
let main argv =
    task {
        let! filename = downloadNode "latest-v14.x" "v14.15.1" (getPlatform()) (getArch())
        // let isValid = verifyChecksum filename "fb23a14c54d7d9ba2ce233262c740f2c04b08e451d1e770ae98b17d01de82b0b"
        // printfn $"Downloaded {filename} checksum matches: {isValid}"
        extractContents (getPlatform()) (Path.GetFullPath filename) (Path.GetFullPath("."))
    }
    |> Async.AwaitTask 
    |> Async.RunSynchronously

    0 // return an integer exit code