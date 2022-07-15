module DumpOneDrive.Auth

open System.Diagnostics
open System.Net.Http.Headers
open System.Threading.Tasks
open Microsoft.Graph
open Microsoft.Identity.Client
open DumpOneDrive.Common

let private getTokenBuilder () =
  let getAuth () =
        let clientId =
            "4a1aa1d5-c567-49d0-ad0b-cd957a47f842"

        let tenant = "common"

        let instance =
            "https://login.microsoftonline.com/"

        let scopes =
            [ "user.read"
              "Files.ReadWrite.All"
              "Sites.Readwrite.All"
              "Sites.Manage.All" ]

        let clientApp =
            PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority($"{instance}{tenant}")
                .WithDefaultRedirectUri()
                .WithBroker()
                .Build()

        let useDefault = false

        (if useDefault then
             clientApp
                 .AcquireTokenSilent(scopes, PublicClientApplication.OperatingSystemAccount)
                 .ExecuteAsync()
         else
             let wndHandle =
                 Process.GetCurrentProcess().MainWindowHandle

             //var accounts = clientApp.GetAccountsAsync();

             clientApp
                 .AcquireTokenInteractive(scopes)
                 //.WithParentActivityOrWindow(wndHandle)
                 .WithPrompt(
                     Prompt.SelectAccount
                 )
                 .ExecuteAsync())
        |> getResult

  let authResult = getAuth ()


  (fun () -> authResult.AccessToken)

let createAuth =
    let bearer = "Bearer"
    let getToken = getTokenBuilder ()

    DelegateAuthenticationProvider (fun request ->
        Task.FromResult(request.Headers.Authorization <- AuthenticationHeaderValue(bearer, getToken ())))
