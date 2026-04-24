namespace SDM.Infrastructure

open System
open System.Text.Json
open System.Text.Json.Serialization
open SDM.Domain
open Microsoft.Data.Sqlite

/// Lightweight SQLite-backed download state persistence.
module DownloadStore =
    let private jsonOptions =
        JsonFSharpOptions.Default().ToJsonSerializerOptions()
        |> fun opts ->
            opts.WriteIndented <- true
            opts

    let private deserializeOrFail<'T> (json: string) =
        match JsonSerializer.Deserialize<'T>(json, jsonOptions) with
        | null -> failwithf "JSON deserialization returned null for type %s" typeof<'T>.Name
        | value -> value

    let initializeDb (connectionString: string) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """
            CREATE TABLE IF NOT EXISTS downloads (
                id          TEXT PRIMARY KEY,
                url         TEXT NOT NULL,
                file_name   TEXT NOT NULL,
                target_path TEXT NOT NULL,
                temp_path   TEXT NOT NULL,
                total_size  INTEGER,
                added_at    TEXT NOT NULL,
                status      TEXT NOT NULL,
                segments    TEXT NOT NULL,
                headers     TEXT NOT NULL,
                cookies     TEXT,
                auth        TEXT NOT NULL,
                hash_algo   TEXT,
                hash_value  TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_downloads_status ON downloads(status);
            CREATE INDEX IF NOT EXISTS idx_downloads_url ON downloads(url);
            CREATE INDEX IF NOT EXISTS idx_downloads_added_at ON downloads(added_at);
            """

        cmd.ExecuteNonQuery() |> ignore

    /// Insert or update a download entry
    let upsert (connectionString: string) (entry: DownloadEntry) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """
            INSERT OR REPLACE INTO downloads
                (id, url, file_name, target_path, temp_path, total_size,
                 added_at, status, segments, headers, cookies, auth,
                 hash_algo, hash_value)
            VALUES
                (@id, @url, @fileName, @targetPath, @tempPath, @totalSize,
                 @addedAt, @status, @segments, @headers, @cookies, @auth,
                 @hashAlgo, @hashValue)
            """

        cmd.Parameters.AddWithValue("@id", string entry.Id) |> ignore
        cmd.Parameters.AddWithValue("@url", entry.Url) |> ignore
        cmd.Parameters.AddWithValue("@fileName", entry.FileName) |> ignore
        cmd.Parameters.AddWithValue("@targetPath", entry.TargetPath) |> ignore
        cmd.Parameters.AddWithValue("@tempPath", entry.TempFolderPath) |> ignore

        cmd.Parameters.AddWithValue(
            "@totalSize",
            entry.TotalSize
            |> Option.map (fun s -> box (int64 s))
            |> Option.defaultValue (box DBNull.Value)
        )
        |> ignore

        cmd.Parameters.AddWithValue("@addedAt", entry.AddedAt.ToString "o") |> ignore

        cmd.Parameters.AddWithValue("@status", JsonSerializer.Serialize(entry.Status, jsonOptions))
        |> ignore

        cmd.Parameters.AddWithValue("@segments", JsonSerializer.Serialize(entry.Segments, jsonOptions))
        |> ignore

        cmd.Parameters.AddWithValue("@headers", JsonSerializer.Serialize(entry.Headers, jsonOptions))
        |> ignore

        cmd.Parameters.AddWithValue(
            "@cookies",
            entry.Cookies |> Option.map box |> Option.defaultValue (box DBNull.Value)
        )
        |> ignore

        cmd.Parameters.AddWithValue("@auth", JsonSerializer.Serialize(entry.Auth, jsonOptions))
        |> ignore

        let hashAlgo, hashVal =
            match entry.Hash with
            | Some(algo, v) -> box (string algo), box v
            | None -> box DBNull.Value, box DBNull.Value

        cmd.Parameters.AddWithValue("@hashAlgo", hashAlgo) |> ignore
        cmd.Parameters.AddWithValue("@hashValue", hashVal) |> ignore

        cmd.ExecuteNonQuery() |> ignore

    /// Read a download entry by ID
    let tryGet (connectionString: string) (id: Guid) : DownloadEntry option =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT * FROM downloads WHERE id = @id"
        cmd.Parameters.AddWithValue("@id", string id) |> ignore

        use reader = cmd.ExecuteReader()

        if reader.Read() then
            Some
                { Id = Guid.Parse(reader.GetString(reader.GetOrdinal "id"))
                  Url = Uri(reader.GetString(reader.GetOrdinal "url"))
                  FileName = reader.GetString(reader.GetOrdinal "file_name")
                  TargetPath = reader.GetString(reader.GetOrdinal "target_path")
                  TempFolderPath = reader.GetString(reader.GetOrdinal "temp_path")
                  TotalSize =
                    if reader.IsDBNull(reader.GetOrdinal "total_size") then
                        None
                    else
                        Some(reader.GetInt64(reader.GetOrdinal "total_size") * 1L<B>)
                  AddedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal "added_at"))
                  Status = deserializeOrFail<DownloadStatus> (reader.GetString(reader.GetOrdinal "status"))
                  Segments = deserializeOrFail<Segment list> (reader.GetString(reader.GetOrdinal "segments"))
                  Headers = deserializeOrFail<Map<string, string>> (reader.GetString(reader.GetOrdinal "headers"))
                  Cookies =
                    if reader.IsDBNull(reader.GetOrdinal "cookies") then
                        None
                    else
                        Some(reader.GetString(reader.GetOrdinal "cookies"))
                  Auth = deserializeOrFail<AuthInfo> (reader.GetString(reader.GetOrdinal "auth"))
                  Hash =
                    if reader.IsDBNull(reader.GetOrdinal "hash_algo") then
                        None
                    else
                        let algo =
                            match reader.GetString(reader.GetOrdinal "hash_algo") with
                            | "MD5" -> MD5
                            | "SHA1" -> SHA1
                            | "SHA256" -> SHA256
                            | _ -> SHA512

                        Some(algo, reader.GetString(reader.GetOrdinal "hash_value")) }
        else
            None

    /// List all downloads, optionally filtered by status pattern
    let listAll (connectionString: string) : DownloadEntry list =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT * FROM downloads ORDER BY added_at DESC"

        use reader = cmd.ExecuteReader()
        let mutable results = []

        while reader.Read() do
            match tryGet connectionString (Guid.Parse(reader.GetString(reader.GetOrdinal "id"))) with
            | Some entry -> results <- entry :: results
            | None -> ()

        results |> List.rev

    /// Delete a downlaod entry by ID
    let delete (connectionString: string) (id: Guid) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM downloads WHERE id = @id"
        cmd.Parameters.AddWithValue("@id", string id) |> ignore
        cmd.ExecuteNonQuery() |> ignore
