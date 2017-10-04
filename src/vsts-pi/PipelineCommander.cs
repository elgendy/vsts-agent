using Microsoft.TeamFoundation.Common;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Configuration;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(PipelineCommander))]
    public interface IPipelineCommander : IAgentService
    {
        Task<int> RunAsync(CommandSettings command);
    }

    public sealed class PipelineCommander : AgentService, IPipelineCommander
    {
        private ILoginManager _loginMgr;
        private ITerminal _term;
        private ManualResetEvent _completedCommand = new ManualResetEvent(false);

        public sealed override void Initialize(IHostContext context)
        {
            base.Initialize(context);

            _loginMgr = context.GetService<ILoginManager>();
            _term = context.GetService<ITerminal>();
        }

        public async Task<int> RunAsync(CommandSettings command)
        {
            try
            {
                Trace.Info(nameof(RunAsync));
                var agentWebProxy = HostContext.GetService<IVstsAgentWebProxy>();
                VssHttpMessageHandler.DefaultWebProxy = agentWebProxy;

                _completedCommand.Reset();
                _term.CancelKeyPress += CtrlCHandler;

                //register a SIGTERM handler
                HostContext.Unloading += Agent_Unloading;
                
                var yamlRunner = HostContext.GetService<IYamlRunner>();

                if (command.Help)
                {
                    PrintUsage(command);
                    return Constants.Agent.ReturnCode.Success;
                }

                if (command.Version)
                {
                    _term.WriteLine(Constants.Agent.Version);
                    return Constants.Agent.ReturnCode.Success;
                }

                if (command.Commit)
                {
                    _term.WriteLine(BuildConstants.Source.CommitHash);
                    return Constants.Agent.ReturnCode.Success;
                }

                if (command.Lint)
                {
                    yamlRunner.Lint(command, HostContext.AgentShutdownToken);
                    return Constants.Agent.ReturnCode.Success;
                }

                if (command.Login)
                {
                    return await _loginMgr.Login(command);                
                }

                if (command.Logout)
                {
                    return _loginMgr.Logout();                    
                }

                if (command.Validate)
                {
                    await yamlRunner.ValidateAsync(command, HostContext.AgentShutdownToken);
                    return Constants.Agent.ReturnCode.Success;
                }

                if (command.Run)
                {
                    await yamlRunner.RunAsync(command, HostContext.AgentShutdownToken);
                    return Constants.Agent.ReturnCode.Success;
                }

                // if no command, print usage and return 1
                PrintUsage(command);
                return Constants.Agent.ReturnCode.TerminatedError;

            }
            catch (Exception ex)
            {
                Trace.Error(ex);
                _term.WriteError(ex.Message);
                return Constants.Agent.ReturnCode.TerminatedError;
            }            
            finally
            {
                _term.CancelKeyPress -= CtrlCHandler;
                HostContext.Unloading -= Agent_Unloading;
                _completedCommand.Set();
            }                
        }

        private void Agent_Unloading(object sender, EventArgs e)
        {
            HostContext.ShutdownAgent(ShutdownReason.UserCancelled);
            _completedCommand.WaitOne(Constants.Agent.ExitOnUnloadTimeout);
        }

        private void CtrlCHandler(object sender, EventArgs e)
        {
            _term.WriteLine("Exiting...");
            HostContext.Dispose();
            Environment.Exit(Constants.Agent.ReturnCode.TerminatedError);
        }      

        private void PrintUsage(CommandSettings command)
        {
            // TODO: loc

            string help = 
@"Commands:
    login        Login and connect with the service.  Only needed once.
    run          Run a pipeline
    lint         Validate syntax of a yaml file
    validate     Validate a pipeline file.  Includes lint and validating referenced tasks and inputs
    logout       Logout

Options:
    --yml        Path to a yaml file.  If not supplied, first file ending in .yml is used
    --offline    Do not attempt to resolve task versions.  Always use what is local task cache.

Examples:
    vsts-pi login --url https://<account>.visualstudio.com --auth pat --token <pat>
    vsts-pi validate --yaml vsts-ci.yml
    vsts-pi run --yaml vsts-ci.yml

Environment Variable Override:
    VSTS_URL     URL of the server or service
    VSTS_PAT     PAT token to use
    
    ";

            _term.WriteLine(help);
        }        
    }
}