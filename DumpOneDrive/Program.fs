open System
open System.IO


open System.Threading
open Microsoft.Graph

open DumpOneDrive.Item
open DumpOneDrive.Common
open DumpOneDrive.Auth

let limit = 5
let semaphore =
        new SemaphoreSlim(limit, limit)

let getFiles path (request: IDriveItemRequestBuilder) =
    async {
        semaphore.Wait()
        request.RequestUrl |> dumpIgnore

        let! response =
            request.Children.Request().GetAsync()
            |> Async.AwaitTask
        semaphore.Release() |> ignore 

        return
            response
            |> Seq.map (fun (i: DriveItem) ->
                { Name = i.Name
                  ID = i.Id
                  URL = i.WebUrl
                  IsFolder = i.Folder <> null
                  Path = path
                  IsFile = i.File <> null
                  Size = Option.ofNullable i.Size
                  Hash =
                    if i.File <> null
                       && i.File.Hashes <> null
                       && i.File.Hashes.QuickXorHash <> null then
                        Some i.File.Hashes.QuickXorHash
                    else
                        None })
            |> List.ofSeq
    }

let expandFolders (graphClient: GraphServiceClient) folders =
    folders
    |> List.map (fun i -> getFiles (Path.Combine(i.Path, i.Name)) (graphClient.Me.Drive.Items[i.ID]))
   // |> (fun p -> Async.Parallel(p, 5))
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.collect Array.ofList
    |> List.ofArray


//let rec getAllFiles root =
// let (folders, files) = List.partition (fun i -> i.IsFolder) root
// match folders with
//  | [] -> files
//  | _ -> let expandedFolders = expandFolders folders
//         (getAllFiles expandedFolders) @ files

let rec getAllFiles2 (graphClient: GraphServiceClient) root =
    seq {
        let (folders, files) =
            root
            |> List.where (fun i -> i.IsFolder || i.IsFile)
            |> List.partition (fun i -> i.IsFolder)

        yield! files

        match folders with
        | [] -> ()
        | _ ->
            let expandedFolders =
                expandFolders graphClient folders

            yield! getAllFiles2 graphClient expandedFolders
            ()
    }

let downLoad (graphClient: GraphServiceClient) dest item =
    
    let folder =  Path.Combine(dest, item.Path)
    let path =
        Path.Combine(folder, item.Name)

    let folderHash = Path.Combine(folder, ".hash")
    let pathHash = Path.Combine(folderHash, item.Name + ".hash")  
    
    if File.Exists path &&
       File.Exists pathHash &&
       match item.Hash with
       | Some hash -> hash = File.ReadAllText(pathHash)
       | None -> false
       then
        $"Exits {path} and hashes match"
    else
        try
            enforceFolderExists folder 
            
            match item.Hash with
            |Some hash ->
                 
                 enforceFolderExists folderHash
                 File.WriteAllText(pathHash, hash)
            |None -> ()     
           
            match item.Size with
            | Some 0L ->
                File.Create(path).Dispose()
                path
            | Some length when length > int64 (250 * 1024 * 1024) -> "Too Long"
            | _ ->

                use stream =
                    graphClient
                        .Me
                        .Drive
                        .Items[ item.ID ]
                        .Content.Request()
                        .GetAsync()
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                use file =
                    new FileStream(path, FileMode.CreateNew)

                stream.CopyTo(file)
                $"{path} Downloaded"


        with
        | ex ->
            $"Excep {path}{ex.ToString()}{if ex.InnerException <> null then
                                              ex.InnerException.ToString()
                                          else
                                              String.Empty}"


let parallelWithThrottle  operation items =

    let continueAction =
        fun () -> semaphore.Release() |> ignore

    items
    |> Seq.iter (fun item ->
        semaphore.Wait()

        let temp =
            async {
                try
                    (operation item) |> dumpIgnore
                finally
                    continueAction ()
            }

        temp |> Async.Start)

let graphClient =
    createAuth |> GraphServiceClient

let items =
    (graphClient.Me.Drive.Root)
    |> getFiles ""
    |> Async.RunSynchronously
    |> List.where (fun i ->
        i.Name.StartsWith("#") |> not
        && (i.Name.Contains("books") || i.Name.Contains("books")))
    |> getAllFiles2 graphClient


let dest = "c:\dump\matze\1drive##"

items
|> parallelWithThrottle (downLoad graphClient dest)

Console.WriteLine "Done!"
Console.Read() |> ignore
