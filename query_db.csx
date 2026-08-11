#r "nuget: Dapper, 2.0.123"
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"
using System;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValutaBot", "signals.db");
if (!File.Exists(dbPath)) {
    Console.WriteLine("DB not found at " + dbPath);
    return;
}

using var conn = new SqliteConnection("Data Source=" + dbPath);
conn.Open();
var count = conn.QuerySingle<int>("SELECT COUNT(*) FROM pending_trades");
Console.WriteLine("Total pending trades: " + count);
if (count > 0) {
    var sample = conn.QueryFirstOrDefault("SELECT verify_at FROM pending_trades LIMIT 1");
    Console.WriteLine("Sample verify_at: " + sample.verify_at);
    Console.WriteLine("Current UTC Now: " + DateTime.UtcNow.ToString("o"));
}
