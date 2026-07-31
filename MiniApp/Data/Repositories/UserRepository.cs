using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace ValutaBot.App.MiniApp.Data.Repositories
{
    public static class UserRepository
    {
        public static async Task<bool> IsUserAllowedAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return false;
            using var conn = DbConnectionFactory.GetConnection();
            return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM allowed_users WHERE chat_id = @chatId)", new { chatId });
        }

        public static async Task<bool> IsAdminAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return false;
            using var conn = DbConnectionFactory.GetConnection();
            return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM admins WHERE chat_id = @chatId)", new { chatId });
        }

        public static async Task<List<long>> GetAdminChatIdsAsync()
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return new List<long>();
            using var conn = DbConnectionFactory.GetConnection();
            var result = await conn.QueryAsync<long>("SELECT chat_id FROM admins");
            return result.ToList();
        }

        public static async Task<int> GetTotalUsersCountAsync()
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return 0;
            using var conn = DbConnectionFactory.GetConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM all_users");
        }

        public static async Task<int> GetAllowedUsersCountAsync()
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return 0;
            using var conn = DbConnectionFactory.GetConnection();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM allowed_users");
        }

        public static async Task AddAllowedUserAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return;
            using var conn = DbConnectionFactory.GetConnection();
            await conn.ExecuteAsync("INSERT INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
        }

        public static async Task AddAdminAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return;
            using var conn = DbConnectionFactory.GetConnection();
            await conn.ExecuteAsync("INSERT INTO admins (chat_id) VALUES (@chatId) ON CONFLICT (chat_id) DO NOTHING", new { chatId });
            await conn.ExecuteAsync("INSERT INTO allowed_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
        }

        public static async Task RemoveAdminAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return;
            using var conn = DbConnectionFactory.GetConnection();
            await conn.ExecuteAsync("DELETE FROM admins WHERE chat_id = @chatId", new { chatId });
        }

        public static async Task RemoveAllowedUserAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return;
            using var conn = DbConnectionFactory.GetConnection();
            await conn.ExecuteAsync("DELETE FROM allowed_users WHERE chat_id = @chatId", new { chatId });
        }

        public static async Task AddAllUserAsync(long chatId)
        {
            if (string.IsNullOrEmpty(DbConnectionFactory.GetConnectionString())) return;
            using var conn = DbConnectionFactory.GetConnection();
            await conn.ExecuteAsync("INSERT INTO all_users (chat_id, created_at) VALUES (@chatId, @now) ON CONFLICT (chat_id) DO NOTHING", new { chatId, now = DateTime.UtcNow.ToString("o") });
        }
    }
}
