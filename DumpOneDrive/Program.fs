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
              Size : Nullable<int64>}

let getResult (task:Task<'a>) =
    task.GetAwaiter().GetResult()
    
let Dump a =
        Console.WriteLine(a.ToString())
        a

let getTokenBuilder () = 
   let getAuth () = 
       let ClientId = "4a1aa1d5-c567-49d0-ad0b-cd957a47f842"
       let Tenant = "common";
       let Instance = "https://login.microsoftonline.com/";
       let Scopes = [ "user.read"; "Files.ReadWrite.All"; "Sites.Readwrite.All"; "Sites.Manage.All"];
       
       let clientApp = PublicClientApplicationBuilder.Create(ClientId)
                        .WithAuthority($"{Instance}{Tenant}")
                        .WithDefaultRedirectUri()
                        .WithBroker()
                        .Build();
       let useDefault = false
           
       (if useDefault then
         clientApp.AcquireTokenSilent(Scopes, PublicClientApplication.OperatingSystemAccount).ExecuteAsync()
       else
         let wndHandle = Process.GetCurrentProcess().MainWindowHandle;

        //var accounts = clientApp.GetAccountsAsync();	

         clientApp.AcquireTokenInteractive(Scopes).WithParentActivityOrWindow(wndHandle) // optional, used to center the browser on the window
                                                                 .WithPrompt(Prompt.SelectAccount)
                                                               .ExecuteAsync())
        |> getResult                                                       

   let mutable authResult  =  getAuth ()
   let mutable lastCreated = DateTime.Now
   
   (fun () ->   if (DateTime.Now - lastCreated).TotalSeconds > float(10 * 60) then
                    authResult <- getAuth()
                    lastCreated <- DateTime.Now
                authResult.AccessToken)

let createAuth = 
       let Bearer = "Bearer"
       let getToken = getTokenBuilder()
       DelegateAuthenticationProvider(fun request -> Task.FromResult(request.Headers.Authorization <- AuthenticationHeaderValue(Bearer, getToken()))) 
      
let graphClient =  createAuth  |> GraphServiceClient

        
let getFiles path (request: IDriveItemRequestBuilder)  =
   async{
      //request.RequestUrl |> Dump
      let! response = request.Children.Request().GetAsync() |> Async.AwaitTask
      return response |> Seq.map (fun (i:DriveItem) ->  {Name = i.Name; ID = i.Id; URL = i.WebUrl; IsFolder = i.Folder <> null; Path = path; IsFile = i.File <> null; Size = i.Size}) 
       |> List.ofSeq 
   }
                  
let expandFolders folders =
    folders |> List.map (fun i -> getFiles (Path.Combine(i.Path, i.Name)) (graphClient.Me.Drive.Items[i.ID])) 
    |> (fun p -> Async.Parallel(p, 5))
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

let rec  getAllFiles2 root =
 seq{
     let (folders, files) = root |> List.where (fun i -> i.IsFolder || i.IsFile) 
                            |> List.partition (fun i -> i.IsFolder) 
     yield! files
     match folders with
      | [] -> ()
      | _ -> let expandedFolders = expandFolders folders
             yield! getAllFiles2 expandedFolders
             ()
    }

let downLoad dest item =
  let destPath = Path.Combine(dest, item.Path, item.Name)
  async {
    if File.Exists destPath then
        ()
    else 
        use! stream =  graphClient.Me.Drive.Items[item.ID].Content.Request().GetAsync() |> Async.AwaitTask
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)) |> ignore
        use file = new FileStream(destPath,FileMode.CreateNew)
      
        stream.CopyToAsync(file) |> Async.AwaitTask |> ignore  
        destPath |> Dump |> ignore
   }

let  parallelWithThrottle limit operation items=
    use semaphore = new SemaphoreSlim(limit, limit)
    let continueAction = fun () -> semaphore.Release() |> ignore
   
    items |> Seq.iter(fun item -> 
        semaphore.Wait()
        async{
            try
                do! (operation item)
            finally
                continueAction()
        } |> Async.Start
      ) 


let items = (graphClient.Me.Drive.Root)  |> getFiles  "" |> Async.RunSynchronously
                    |> List.where (fun i -> i.Name.StartsWith("#") |> not && (i.Name.Contains("books") || i.Name.Contains("iT")))
                    |> getAllFiles2 
                    
let dest = "d:\matze\1Drive###"
items |> parallelWithThrottle 5 (downLoad dest) 



                    
          