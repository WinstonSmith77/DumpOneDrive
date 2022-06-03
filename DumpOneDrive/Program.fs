open System
open System.IO
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open System.Text.Json.Serialization
open System.Diagnostics
open Microsoft.Graph
open Microsoft.Identity.Client

type Item = { Name  : string;
              Path : string
              ID : string;
              URL    : string 
              [<JsonIgnore>]
              IsFolder : bool
              [<JsonIgnore>]
              IsFile : bool
              [<JsonIgnore>]
              Size : Nullable<int64>
              [<JsonIgnore>]
              Hash : string}

let getResult (task:Task<'a>) =
    task.GetAwaiter().GetResult()
    
let dump a =
        Console.WriteLine(a.ToString())
        a
let dumpIgnore a = (dump a) |> ignore        

let getTokenBuilder () = 
   let getAuth () = 
       let clientId = "4a1aa1d5-c567-49d0-ad0b-cd957a47f842"
       let tenant = "common";
       let instance = "https://login.microsoftonline.com/";
       let scopes = [ "user.read"; "Files.ReadWrite.All"; "Sites.Readwrite.All"; "Sites.Manage.All"];
       
       let clientApp = PublicClientApplicationBuilder.Create(clientId)
                        .WithAuthority($"{instance}{tenant}")
                        .WithDefaultRedirectUri()
                        .WithBroker()
                        .Build();
       
       let useDefault = false
           
       (if useDefault then
         clientApp.AcquireTokenSilent(scopes, PublicClientApplication.OperatingSystemAccount).ExecuteAsync()
       else
         let wndHandle = Process.GetCurrentProcess().MainWindowHandle;

        //var accounts = clientApp.GetAccountsAsync();	

         clientApp.AcquireTokenInteractive(scopes)
                          //.WithParentActivityOrWindow(wndHandle)
                          .WithPrompt(Prompt.SelectAccount)
                          .ExecuteAsync()
        )
        |> getResult                                                       

   let  authResult  =  getAuth ()
   
   
   (fun () ->  authResult.AccessToken)

let createAuth = 
       let bearer = "Bearer"
       let getToken = getTokenBuilder()
       DelegateAuthenticationProvider(fun request -> Task.FromResult(request.Headers.Authorization <- AuthenticationHeaderValue(bearer, getToken()))) 

let getFiles path (request: IDriveItemRequestBuilder)  =
   async{
      request.RequestUrl |> dumpIgnore
      let! response = request.Children.Request().GetAsync() |> Async.AwaitTask
      return response |> Seq.map (fun (i:DriveItem) ->
        {Name = i.Name
         ID = i.Id
         URL = i.WebUrl
         IsFolder = i.Folder <> null
         Path = path
         IsFile = i.File <> null
         Size = i.Size
         Hash = if i.File <> null then i.File.Hashes.QuickXorHash else ""}) 
       |> List.ofSeq 
   }
               
let expandFolders (graphClient:GraphServiceClient) folders =
    folders |> List.map (fun i -> getFiles (Path.Combine(i.Path, i.Name)) (graphClient.Me.Drive.Items[i.ID])) 
    |> (fun p -> Async.Parallel(p, 10))
   // |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.collect Array.ofList
    |> List.ofArray
 

//let rec getAllFiles root =
// let (folders, files) = List.partition (fun i -> i.IsFolder) root
// match folders with
//  | [] -> files
//  | _ -> let expandedFolders = expandFolders folders
//         (getAllFiles expandedFolders) @ files

let rec  getAllFiles2  (graphClient:GraphServiceClient) root =
 seq{
     let (folders, files) = root |> List.where (fun i -> i.IsFolder || i.IsFile) 
                            |> List.partition (fun i -> i.IsFolder) 
     yield! files
     match folders with
      | [] -> ()
      | _ -> let expandedFolders = expandFolders  graphClient folders
             yield! getAllFiles2 graphClient expandedFolders
             ()
    }

let downLoad  (graphClient:GraphServiceClient) dest item =
  let path = Path.Combine(dest, item.Path, item.Name)
  async {
    if File.Exists path then
        return $"Exits {path}"
    else 
       try
            use! stream =  graphClient.Me.Drive.Items[item.ID].Content.Request().GetAsync() |> Async.AwaitTask
            Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
            use file = new FileStream(path,FileMode.CreateNew)
          
            stream.CopyTo(file) 
            return path
       with
        |  ex -> return $"Excep {ex.ToString()}"
   }

let  parallelWithThrottle limit operation items=
    use semaphore = new SemaphoreSlim(limit, limit)
    let continueAction = fun () -> semaphore.Release() |> ignore
   
    items |> Seq.iter(fun item -> 
        semaphore.Wait()
        let temp =
            async{
                try
                    let! (result:string)  = (operation item)
                    result |> dumpIgnore
                finally
                    continueAction()
            }
        temp |> Async.Start
      ) 

let graphClient =  createAuth  |> GraphServiceClient

let items = (graphClient.Me.Drive.Root)  |> getFiles  "" |> Async.RunSynchronously
                    |> List.where (fun i -> i.Name.StartsWith("#") |> not && (i.Name.Contains("books") || i.Name.Contains("books")))
                    |> getAllFiles2  graphClient
                    

let dest = "c:\dump\matze\1drive#"
items |> parallelWithThrottle 10 (downLoad graphClient dest)

Console.WriteLine "Done!"
Console.Read() |> ignore 



                    
          