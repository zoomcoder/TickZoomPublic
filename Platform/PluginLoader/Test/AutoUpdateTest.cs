#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion


using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TickZoom.Api;
using TickZoom.Update;

namespace TickZoom.Utilities
{

	
	[TestFixture]
	public class AutoUpdateTest
	{
		private static readonly Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private string testVersion = "1.2.51.51756";
		private string appData;
		private string dllFolder;
		[TestFixtureSetUp]
		public void Setup() {
			appData = Factory.Settings["AppDataFolder"];
			dllFolder =  appData + Path.DirectorySeparatorChar +
				@"AutoUpdate\" + testVersion + Path.DirectorySeparatorChar;
		}
		[Test]
		public void TestKeyNotFound()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.UserKey = "BadKey";
			updater.CurrentVersion = testVersion;
			bool retVal = updater.DownloadFile("TickZoomEngine.dll");
			Assert.IsFalse(retVal,"did download");
			Assert.IsTrue(updater.Message.StartsWith("Your user key was not found. Please verify that you have a valid key.") );
		}
		[Test]
		public void TestBadKey()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.CurrentVersion = testVersion;
			updater.UserKey = @"Bad Key";
			bool retVal = updater.DownloadFile("TickZoomEngine.dll");
			Assert.IsFalse(retVal,"did download");
			Assert.AreEqual("Your user key was not found. Please verify that you have a valid key.\n", updater.Message);
		}
		
		[Test]
		public void TestBadFile()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.CurrentVersion = testVersion;
			bool retVal = updater.DownloadFile("TickZoomEngine.dllXXX");
			Assert.IsFalse(retVal,"did download");
			Assert.IsTrue(updater.Message.StartsWith("File TickZoomEngine"));
			Assert.IsTrue(updater.Message.EndsWith("not found for membership Gold.\n"));
		}

		[Test]
		public void TestFileListSuccess()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.CurrentVersion = testVersion;
			string[] files = updater.GetFileList();
            Assert.NotNull(files,"result from getfiles");
			Assert.AreEqual(2,files.Length);
            
            Assert.IsTrue(files[0].StartsWith("ProviderCommon-1.2.51.51756.dll.zip"), files[0]);
            Assert.IsTrue(files[1].StartsWith("TickZoomEngine-1.2.51.51756.dll.zip"), files[1]);
		}
		
		[Test]
		public void TestFileListFailure()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.UserKey = "";
			updater.CurrentVersion = testVersion;
			string[] files = updater.GetFileList();
			Assert.IsNull(files);
		}
		
		[Test]
		public void TestFreeEngineFailure()
		{
			AutoUpdate updater = new AutoUpdate();
			updater.UserKey = "";
			updater.CurrentVersion = testVersion;
			bool retVal = updater.DownloadFile("TickZoomEngine-"+testVersion+".dll");
			Assert.IsFalse(retVal,"did download");
		}
		
		[Test]
		public void TestFreeEngineSuccess()
		{
			AutoUpdate updater = new AutoUpdate();
//			updater.UserKey = "";
			updater.CurrentVersion = testVersion;
			bool retVal = updater.DownloadFile("TickZoomEngine-"+testVersion+".dll");
			Assert.IsTrue(retVal,"did download");
			Assert.AreEqual("", updater.Message);
			string appData = Factory.Settings["AppDataFolder"];
			string dllFile =  appData + Path.DirectorySeparatorChar +
				@"AutoUpdate\" + updater.CurrentVersion + Path.DirectorySeparatorChar +
				"TickZoomEngine-"+updater.CurrentVersion+".dll.zip";
			Assert.IsTrue(File.Exists(dllFile));
		}

		[Test]
		public void TestFreeUpdateAllFailure()
		{
			while( Directory.Exists(dllFolder)) {
				try {
					Directory.Delete(dllFolder,true);
					break;				
				} catch( IOException) {
				}
			}
			AutoUpdate updater = new AutoUpdate();
			updater.UserKey = "";
			updater.CurrentVersion = testVersion;
			bool retVal = updater.UpdateAll();
			Assert.IsFalse(retVal,"did download");
			Assert.AreEqual("Your user key was not found. Please verify that you have a valid key.\n", updater.Message);
		}
		
		[Test]
		public void TestFreeUpdateAll()
		{
			while( Directory.Exists(dllFolder)) {
				try {
					Directory.Delete(dllFolder,true);
					break;				
				} catch( IOException) {
				}
			}
			TestUpdateAll(null,"",true);
			TestUpdateAll(null,"",false);
		}
		
		[Test]
		public void TestGoldUpdateAll()
		{
			while( Directory.Exists(dllFolder)) {
				try {
					Directory.Delete(dllFolder,true);
					break;				
				} catch( IOException) {
				}
			}
			TestUpdateAll(null,"Engine",true);
			TestUpdateAll(null,"Engine",false);
		}
		
		public void TestUpdateAll(string userKey, string currentModel, bool isExpectedDownload)
		{
			AutoUpdate updater = new AutoUpdate();
			if( userKey != null) {
				updater.UserKey = userKey;
			}
			updater.CurrentVersion = testVersion;
			bool retVal = updater.UpdateAll();
			Assert.AreEqual(isExpectedDownload,retVal,"did download");
			Assert.AreEqual("", updater.Message);
			if( !isExpectedDownload) {
				return;
			}
			string dllFile = dllFolder + "TickZoomEngine-"+updater.CurrentVersion+".dll";
			Assert.IsTrue(File.Exists(dllFile));
		    var assemblyName = AssemblyName.GetAssemblyName(dllFile);
		    Assert.AreEqual(assemblyName.Version, new Version(testVersion));
			dllFile = dllFolder + "ProviderCommon-"+updater.CurrentVersion+".dll";
			Assert.IsTrue(File.Exists(dllFile));
		}
	}
}
