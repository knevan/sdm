namespace SDM.Infrastructure

open System
open System.Text.Json
open SDM.Domain
open Microsoft.Data.Sqlite

/// Lightweight SQLite-backed download state persistence.
module DownloadStore =
    /// Read a required string column by name
    let private str (reader: SqliteDataReader) (col: string) = reader.GetString(reader.GetOrdinal col)

    /// Read a nullable string column as F# Option
    let private strOpt (reader: SqliteDataReader) (col: string) =
        let ordinal = reader.GetOrdinal col

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetString ordinal)

    /// Read a nullable int64 column as F# Option
    let private int64Opt (reader: SqliteDataReader) (col: string) =
        let ordinal = reader.GetOrdinal col

        if reader.IsDBNull ordinal then
            None
        else
            Some(reader.GetInt64 ordinal)

    /// Deserialize a required JSON text column into the target F# type.
    /// NonNull assertion is safe because these columns are NOT NULL in the schema,
    /// and JsonFSharpConverter always produces valid F# values.
    let private json<'T> (reader: SqliteDataReader) (col: string) (opts: JsonSerializerOptions) : 'T =
        let raw = reader.GetString(reader.GetOrdinal col)

        match JsonSerializer.Deserialize<'T>(raw, opts) with
        | null -> failwith $"Unexpected null deserialization for column {col}"
        | result -> result

    /// Box a value for SQLite param
    let private optParam (value: 'T option) : obj =
        match value with
        | Some v -> (box v) |> Option.ofObj |> Option.defaultValue DBNull.Value
        | None -> DBNull.Value

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
        let opts = JsonConfig.options

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

        let hashAlgo, hashVal =
            match entry.Hash with
            | Some(algo, v) -> box (string algo), box v
            | None -> box DBNull.Value, box DBNull.Value

        cmd.Parameters.AddWithValue("@id", string entry.Id) |> ignore
        cmd.Parameters.AddWithValue("@url", string entry.Url) |> ignore
        cmd.Parameters.AddWithValue("@fileName", entry.FileName) |> ignore
        cmd.Parameters.AddWithValue("@targetPath", entry.TargetPath) |> ignore
        cmd.Parameters.AddWithValue("@tempPath", entry.TempFolderPath) |> ignore

        cmd.Parameters.AddWithValue("@totalSize", optParam (entry.TotalSize |> Option.map int64))
        |> ignore

        cmd.Parameters.AddWithValue("@addedAt", entry.AddedAt.ToString "o") |> ignore

        cmd.Parameters.AddWithValue("@status", JsonSerializer.Serialize<DownloadStatus>(entry.Status, opts))
        |> ignore

        cmd.Parameters.AddWithValue("@segments", JsonSerializer.Serialize<Segment list>(entry.Segments, opts))
        |> ignore

        cmd.Parameters.AddWithValue("@headers", JsonSerializer.Serialize<Map<string, string>>(entry.Headers, opts))
        |> ignore

        cmd.Parameters.AddWithValue("@cookies", optParam entry.Cookies) |> ignore

        cmd.Parameters.AddWithValue("@auth", JsonSerializer.Serialize<AuthInfo>(entry.Auth, opts))
        |> ignore

        cmd.Parameters.AddWithValue("@hashAlgo", hashAlgo) |> ignore
        cmd.Parameters.AddWithValue("@hashValue", hashVal) |> ignore

        cmd.ExecuteNonQuery() |> ignore

    /// Parse a single row from a SqliteDataReader into a DownloadEntry.
    let private readEntry (reader: SqliteDataReader) =
        let opts = JsonConfig.options

        { Id = Guid.Parse(str reader "id")
          Url = Uri(str reader "url")
          FileName = str reader "file_name"
          TargetPath = str reader "target_path"
          TempFolderPath = str reader "temp_path"
          TotalSize = int64Opt reader "total_size" |> Option.map (fun s -> s * 1L<B>)
          AddedAt = DateTime.Parse(str reader "added_at")
          Status = json<DownloadStatus> reader "status" opts
          Segments = json<Segment list> reader "segments" opts
          Headers = json<Map<string, string>> reader "headers" opts
          Cookies = strOpt reader "cookies"
          Auth = json<AuthInfo> reader "auth" opts
          Hash =
            match strOpt reader "hash_algo" with
            | None -> None
            | Some algoStr ->
                let algo =
                    match algoStr with
                    | "MD5" -> MD5
                    | "SHA1" -> SHA1
                    | "SHA256" -> SHA256
                    | _ -> SHA512

                Some(algo, str reader "hash_value") }


    /// Read a download entry by ID
    let tryGet (connectionString: string) (id: Guid) : DownloadEntry option =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT * FROM downloads WHERE id = @id"
        cmd.Parameters.AddWithValue("@id", string id) |> ignore

        use reader = cmd.ExecuteReader()

        if reader.Read() then Some(readEntry reader) else None

    /// List all downloads, optionally filtered by status pattern
    let listAll (connectionString: string) : DownloadEntry list =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT * FROM downloads ORDER BY added_at DESC"

        use reader = cmd.ExecuteReader()

        [ while reader.Read() do
              readEntry reader ]

    /// Delete a downlaod entry by ID
    let delete (connectionString: string) (id: Guid) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()

        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM downloads WHERE id = @id"
        cmd.Parameters.AddWithValue("@id", string id) |> ignore
        cmd.ExecuteNonQuery() |> ignore
