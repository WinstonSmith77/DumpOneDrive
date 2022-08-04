module DumpOneDrive.Item

type Item =
    { Name: string
      Path: string
      ID: string
      URL: string
      IsFolder: bool
      IsFile: bool
      Size: int64 Option
      Hash: string Option }
