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
        private SqliteTransaction _currentTransaction;   // 当前活跃事务，DAO 写操作自动绑定

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

        /// <summary>
        /// 初始化数据库连接，返回是否成功
        /// </summary>
        public bool Init()
        {
            if (_connection != null) return true; // 已初始化

            // 持久化路径：重启游戏数据不丢失
            _runtimeDbPath = Path.Combine(Application.persistentDataPath, "game.db");

            // 源库校验：不存在则无法初始化，避免 File.Copy 抛异常
            string sourcePath = Path.Combine(Application.streamingAssetsPath, "game.db");
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[SqliteManager] 源数据库不存在：{sourcePath}\n" +
                               "请将初始库 game.db 放入 StreamingAssets 目录，或确认构建时已包含该文件。");
                return false;
            }

            // 首次启动复制初始配置库到持久化目录
            if (!File.Exists(_runtimeDbPath))
            {
                try
                {
                    File.Copy(sourcePath, _runtimeDbPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SqliteManager] 复制数据库失败：{e.Message}");
                    return false;
                }
            }

            try
            {
                _connection = new SqliteConnection($"Data Source={_runtimeDbPath}");
                _connection.Open();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SqliteManager] 打开数据库失败：{e.Message}");
                return false;
            }

            return true;
        }

        // 执行增删改SQL（自动绑定当前事务，无事务时直接提交）
        public void ExecuteNonQuery(string sql)
        {
            using var cmd = new SqliteCommand(sql, _connection);
            if (_currentTransaction != null)
                cmd.Transaction = _currentTransaction;
            cmd.ExecuteNonQuery();
        }

        // 执行查询SQL（自动绑定当前事务）
        public SqliteDataReader ExecuteQuery(string sql)
        {
            using var cmd = new SqliteCommand(sql, _connection);
            if (_currentTransaction != null)
                cmd.Transaction = _currentTransaction;
            return cmd.ExecuteReader();
        }

        /// <summary>
        /// 开启事务——统一由 SqliteManager 管理，后续 ExecuteNonQuery/ExecuteQuery 自动绑定
        /// </summary>
        public void BeginTransaction()
        {
            if (_connection == null)
            {
                Debug.LogError("[SqliteManager] 数据库未初始化，无法开启事务");
                return;
            }
            if (_currentTransaction != null)
            {
                Debug.LogWarning("[SqliteManager] 已有未提交的事务，忽略重复开启");
                return;
            }
            _currentTransaction = _connection.BeginTransaction();
        }

        /// <summary>
        /// 提交当前事务
        /// </summary>
        public void CommitTransaction()
        {
            if (_currentTransaction == null)
            {
                Debug.LogWarning("[SqliteManager] 没有开启的事务，忽略提交");
                return;
            }
            _currentTransaction.Commit();
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }

        /// <summary>
        /// 回滚当前事务
        /// </summary>
        public void RollbackTransaction()
        {
            if (_currentTransaction == null)
            {
                Debug.LogWarning("[SqliteManager] 没有开启的事务，忽略回滚");
                return;
            }
            _currentTransaction.Rollback();
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }

        private void OnApplicationQuit()
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
            _connection?.Close();
        }
    }
}
