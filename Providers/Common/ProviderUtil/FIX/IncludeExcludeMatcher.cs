using TickZoom.Api;

namespace TickZoom.FIX
{
    public class IncludeExcludeMatcher : LogAware
    {
        private readonly Log log;
        private volatile bool trace;
        private volatile bool debug;
        public virtual void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private string[] includes = new string[] { "*" };
        private string[] excludes = new string[0];
        public IncludeExcludeMatcher(string includeString, string excludeString)
        {
            log = Factory.SysLog.GetLogger(typeof(IncludeExcludeMatcher) + "." + GetType().Name);
            log.Register(this);
            includes = ParseList(includeString);
            if( includes.Length == 0)
            {
                includes = new string[] { "*" };
            }
            excludes = ParseList(excludeString);
        }
        private string[] ParseList(string list)
        {
            string[] stringList;
            if (string.IsNullOrEmpty(list))
            {
                stringList = new string[0];
            }
            else
            {
                stringList = list.Split(new char[] { ',' });
                for (var i = 0; i < stringList.Length; i++)
                {
                    stringList[i] = stringList[i].Trim();
                }
            }
            return stringList;
        }

        public bool Compare(string value)
        {
            var result = false;
            foreach (var mask in includes)
            {
                if (value.CompareWildcard(mask, true))
                {
                    if (debug) log.Debug(value + " matched include mask " + mask);
                    result = true;
                    break;
                }
            }
            if (!result) return result;
            foreach (var mask in excludes)
            {
                if (value.CompareWildcard(mask, true))
                {
                    if (debug) log.Debug("Excluding " + value + " because of mask " + mask);
                    result = false;
                    break;
                }
            }
            return result;
        }
    }
}