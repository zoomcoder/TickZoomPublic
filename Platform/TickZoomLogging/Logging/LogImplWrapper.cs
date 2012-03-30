using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using log4net.Core;
using TickZoom.Api;

namespace TickZoom.Logging
{
    internal class LogImplWrapper : log4net.Core.LogImpl {
        private Level m_levelVerbose;
        private Level m_levelTrace;
        private Level m_levelDebug;
        private Level m_levelInfo;
        private Level m_levelNotice;
        private Level m_levelWarn;
        private Level m_levelError;
        private Level m_levelFatal;
        private bool verbose;
        private bool trace;
        private bool debug;
        private bool info;
        private bool notice;
        private bool warn;
        private bool error;
        private bool fatal;
        private bool isInitialized = false;
        private List<WeakReference> logListeners = new List<WeakReference>();
			
        public LogImplWrapper(ILogger logger) : base( logger) {
        }
			
        private static readonly Level[] levels = new Level[] {
                                                                 Level.Verbose,
                                                                 Level.Trace,
                                                                 Level.Debug,
                                                                 Level.Info,
                                                                 Level.Notice,
                                                                 Level.Warn,
                                                                 Level.Error,
                                                                 Level.Fatal
                                                             };

        public void Register(LogAware aware)
        {
            var reference = new WeakReference(aware);
            logListeners.Add(reference);
        }

        internal void NotifyListeners()
        {
            for (int i = logListeners.Count - 1; i >= 0; i--)
            {
                var reference = logListeners[i];
                if( reference != null)
                {
                    if (!reference.IsAlive)
                    {
                        logListeners.RemoveAt(i);
                    }
                    else
                    {
                        var hardReference = reference.Target;
                        if( hardReference != null)
                        {
                            var logAware = (LogAware)reference.Target;
                            logAware.RefreshLogLevel();
                        }
                    }
                }
            }
        }

        public void ReloadLevels()
        {
            ReloadLevels(base.Logger.Repository);
        }
			
        protected override void ReloadLevels(log4net.Repository.ILoggerRepository repository)
        {
            base.ReloadLevels(repository);
            m_levelVerbose = repository.LevelMap.LookupWithDefault(Level.Verbose);
            m_levelTrace = repository.LevelMap.LookupWithDefault(Level.Trace);
            m_levelDebug = repository.LevelMap.LookupWithDefault(Level.Debug);
            m_levelInfo = repository.LevelMap.LookupWithDefault(Level.Info);
            m_levelNotice = repository.LevelMap.LookupWithDefault(Level.Notice);
            m_levelWarn = repository.LevelMap.LookupWithDefault(Level.Warn);
            m_levelError = repository.LevelMap.LookupWithDefault(Level.Error);
            m_levelFatal = repository.LevelMap.LookupWithDefault(Level.Fatal);
            FindAnyEnabled();
            verbose = IsAnyEnabledFor(m_levelVerbose);
            trace = IsAnyEnabledFor(m_levelTrace);
            debug = IsAnyEnabledFor(m_levelDebug);
            info = IsAnyEnabledFor(m_levelInfo);
            notice = IsAnyEnabledFor(m_levelNotice);
            warn = IsAnyEnabledFor(m_levelWarn);
            error = IsAnyEnabledFor(m_levelError);
            fatal = IsAnyEnabledFor(m_levelFatal);
            isInitialized = true;
            NotifyListeners();
        }
			
        private void TryReloadLevels() {
            if( !isInitialized) {
                ReloadLevels(Logger.Repository);
            }
        }
			
        public bool IsVerboseEnabled {
            get { TryReloadLevels(); return verbose; }
        }
			
        public bool IsTraceEnabled {
            get { TryReloadLevels(); return trace; }
        }
			
        public override bool IsDebugEnabled {
            get { TryReloadLevels(); return debug; }
        }
			
        public override bool IsInfoEnabled {
            get { TryReloadLevels(); return info; }
        }
			
        public bool IsNoticeEnabled {
            get { TryReloadLevels(); return notice; }
        }
			
        public override bool IsWarnEnabled {
            get { TryReloadLevels(); return warn; }
        }
			
        public override bool IsErrorEnabled {
            get { TryReloadLevels(); return error; }
        }
			
        public override bool IsFatalEnabled {
            get { TryReloadLevels(); return fatal; }
        }
			
        private bool IsAnyEnabledFor(Level level) {
            while( true) {
                try {
                    return CheckAnyEnabled(level);
                } catch( InvalidOperationException) {
                } catch( KeyNotFoundException) {
                }
            }
        }

        private Dictionary<Level,int> childrenByLevel = new Dictionary<Level, int>();
        private object childrenLocker = new object();
			
        private void FindAnyEnabled() {
            lock(childrenLocker)
            {
                foreach( var level in levels) {
                    childrenByLevel[level] = 0;
                }
                ILogger[] loggers = null;
                while( loggers == null) {
                    try {
                        loggers = Logger.Repository.GetCurrentLoggers();
                    } catch( InvalidOperationException) {
    						
                    }
                }
                for( var i=0; i<loggers.Length; i++) {
                    var child = loggers[i];
                    foreach( var level in levels) {
                        if( child.IsEnabledFor(level)) {
                            if( child.Name.StartsWith(Logger.Name)) {
                                childrenByLevel[level]++;
                            } else if( child.Name.Contains("*") || child.Name.Contains("?")) {
                                var wildcard = new Wildcard(child.Name, RegexOptions.IgnoreCase);
                                if( wildcard.IsMatch(Logger.Name)) {
                                    childrenByLevel[level]++;
                                }
                            }
                        }
                    }
                }
            }
        }
			
        private bool CheckAnyEnabled(Level level) {
            return childrenByLevel[level] > 0;
        }
    }
}