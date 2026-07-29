using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ValutaBot.MiniApp.Data;

public static class LocalTickDatabase
{
    private static readonly string DbPath;
    private static readonly string ConnectionString;

    static LocalTickDatabase()
    {
        string dataDir = Path.Combine("ml_service", "data");
        if (!Directory.Exists(dataDir)) 
        {
            Directory.CreateDirectory(dataDir);
        }
        
        DbPath = Path.Combine(dataDir, "ValutaTicks.db");
        ConnectionString = $"Data Source={DbPath};Cache=Shared";
        InitializeDatabase();
    }

    private static void InitializeDatabase()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        
        string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS SubminuteCandles (
                Asset TEXT NOT NULL,
                Interval TEXT NOT NULL,
                OpenTime INTEGER NOT NULL,
                Open REAL NOT NULL,
                High REAL NOT NULL,
                Low REAL NOT NULL,
                Close REAL NOT NULL,
                Volume REAL NOT NULL,
                PRIMARY KEY (Asset, Interval, OpenTime)
            );
            CREATE INDEX IF NOT EXISTS IDX_Asset_Interval ON SubminuteCandles(Asset, Interval);
        ";
        connection.Execute(createTableQuery);
    }

    public static async Task SaveCandleAsync(string asset, string interval, long openTimeMs, double open, double high, double low, double close, double volume)
    {
        try 
        {
            using var connection = new SqliteConnection(ConnectionString);
            string insertQuery = @"
                INSERT INTO SubminuteCandles (Asset, Interval, OpenTime, Open, High, Low, Close, Volume)
                VALUES (@Asset, @Interval, @OpenTime, @Open, @High, @Low, @Close, @Volume)
                ON CONFLICT(Asset, Interval, OpenTime) DO UPDATE SET
                    High = MAX(High, excluded.High),
                    Low = MIN(Low, excluded.Low),
                    Close = excluded.Close,
                    Volume = Volume + excluded.Volume;
            ";
            
            await connection.ExecuteAsync(insertQuery, new { 
                Asset = asset, 
                Interval = interval, 
                OpenTime = openTimeMs, 
                Open = open, 
                High = high, 
                Low = low, 
                Close = close, 
                Volume = volume 
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalTickDatabase] Error saving candle: {ex.Message}");
        }
    }
}
