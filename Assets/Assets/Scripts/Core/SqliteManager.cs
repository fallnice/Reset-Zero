using UnityEngine;
using Mono.Data.Sqlite;
using System.IO;

namespace Core
{
    // 执行顺序设为 -100，确保在 GameRoot（默认 0）之前完成 Awake
    [DefaultExecutionOrder(-100)]
    public class SqliteManager : MonoBehaviour
    {
        public static SqliteManager Instance { get; private set; }

        private SqliteConnection _connection;
        private string _runtimeDbPath;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // DontDestroyOnLoad 只对根级 GameObject 生效，先脱离父级
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        public void Init()
        {
            if (_connection != null) return; // 已初始化则跳过

            // 持久化路径：重启游戏数据不丢失
            _runtimeDbPath = Path.Combine(Application.persistentDataPath, "game.db");

            // 诊断日志：打印实际路径
           // Debug.Log($"[SqliteManager] persistentDataPath = {Application.persistentDataPath}");
            //Debug.Log($"[SqliteManager] 运行时数据库路径 = {_runtimeDbPath}");
            //Debug.Log($"[SqliteManager] 运行时库是否存在 = {File.Exists(_runtimeDbPath)}");

            string sourcePath = Path.Combine(Application.streamingAssetsPath, "game.db");
            //Debug.Log($"[SqliteManager] 源数据库路径 = {sourcePath}");
            //Debug.Log($"[SqliteManager] 源库是否存在 = {File.Exists(sourcePath)}");

            // 首次启动复制初始配置库
            if (!File.Exists(_runtimeDbPath))
            {
                //Debug.Log("[SqliteManager] 复制新数据库...");
                File.Copy(sourcePath, _runtimeDbPath);
            }

            _connection = new SqliteConnection($"Data Source={_runtimeDbPath}");
            _connection.Open();

            // 诊断：列出所有表
            //var reader = ExecuteQuery("SELECT name FROM sqlite_master WHERE type='table'");
           // while (reader.Read())
            //{
              //  Debug.Log($"[SqliteManager] 数据库中包含表：{reader.GetString(0)}");
            //}
            //reader.Close();
        }

        // 执行增删改SQL
        public void ExecuteNonQuery(string sql)
        {
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.ExecuteNonQuery();
        }

        // 执行查询SQL
        public SqliteDataReader ExecuteQuery(string sql)
        {
            using var cmd = new SqliteCommand(sql, _connection);
            return cmd.ExecuteReader();
        }

        // 开启事务
        public SqliteTransaction BeginTransaction()
        {
            return _connection.BeginTransaction();
        }

        private void OnApplicationQuit()
        {
            _connection?.Close();
        }
    }
}
