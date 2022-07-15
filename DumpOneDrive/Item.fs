module DumpOneDrive.Item

open System.Text.Json.Serialization

type Item =
    { Name: string
      Path: string
      ID: string
      URL: string
      [<JsonIgnore>]
      IsFolder: bool
      [<JsonIgnore>]
      IsFile: bool
      [<JsonIgnore>]
      Size: int64 Option
      [<JsonIgnore>]
      Hash: string Option }
