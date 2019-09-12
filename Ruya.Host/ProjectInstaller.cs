using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Windows.Forms;

namespace Ruya.Host
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
            ModifyComponent();
        }

        private void ModifyComponent()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string path = Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrEmpty(path))
            {
                Directory.SetCurrentDirectory(path);
            }

            serviceProcessInstaller1.Account = ServiceAccount.LocalSystem;
            serviceProcessInstaller1.Password = null;
            serviceProcessInstaller1.Username = null;

            serviceInstaller1.StartType = ServiceStartMode.Automatic;

            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            serviceInstaller1.ServiceName = Program.ServiceName;
            serviceInstaller1.Description = fileVersionInfo.ProductVersion;
        }

        public override void Commit(IDictionary savedState)
        {
            base.Commit(savedState);
            
            try
            {
                var serviceController = new ServiceController(Program.ServiceName);
                serviceController.Start();
                serviceController.WaitForStatus(ServiceControllerStatus.Running);
            }
            // ReSharper disable once CatchAllClause
            catch (Exception)
            {
                const string message = "Service couldn't be started, you will have to do it manually";
                MessageBox.Show(message);
            }
        }        
    }
}