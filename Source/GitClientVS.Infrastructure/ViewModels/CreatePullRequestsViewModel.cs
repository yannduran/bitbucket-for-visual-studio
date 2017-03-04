﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GitClientVS.Contracts.Events;
using GitClientVS.Contracts.Interfaces.Services;
using GitClientVS.Contracts.Interfaces.ViewModels;
using GitClientVS.Contracts.Interfaces.Views;
using GitClientVS.Contracts.Models.GitClientModels;
using GitClientVS.Infrastructure.Extensions;
using ReactiveUI;

namespace GitClientVS.Infrastructure.ViewModels
{
    [Export(typeof(ICreatePullRequestsViewModel))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class CreatePullRequestsViewModel : ViewModelBase, ICreatePullRequestsViewModel
    {
        private readonly IGitClientService _gitClientService;
        private readonly IGitService _gitService;
        private readonly IPageNavigationService<IPullRequestsWindow> _pageNavigationService;
        private readonly IEventAggregatorService _eventAggregator;
        private ReactiveCommand<Unit> _initializeCommand;
        private bool _isLoading;
        private string _errorMessage;
        private ReactiveCommand<Unit> _createNewPullRequestCommand;
        private IEnumerable<GitBranch> _remoteBranches;
        private IEnumerable<GitLocalBranch> _localBranches;
        private GitLocalBranch _sourceBranch;
        private GitBranch _destinationBranch;
        private string _description;
        private string _Title;
        private bool _closeSourceBranch;
        private string _message;
        private GitRemoteRepository _currentRepo;

        public string PageTitle { get; } = "Create New Pull Request";

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set { this.RaiseAndSetIfChanged(ref _errorMessage, value); }
        }

        public IEnumerable<GitLocalBranch> LocalBranches
        {
            get { return _localBranches; }
            set { this.RaiseAndSetIfChanged(ref _localBranches, value); }
        }

        public IEnumerable<GitBranch> RemoteBranches
        {
            get { return _remoteBranches; }
            set { this.RaiseAndSetIfChanged(ref _remoteBranches, value); }
        }


        [Required]

        public GitLocalBranch SourceBranch
        {
            get { return _sourceBranch; }
            set { this.RaiseAndSetIfChanged(ref _sourceBranch, value); }
        }


        [Required]
        public GitBranch DestinationBranch
        {
            get { return _destinationBranch; }
            set { this.RaiseAndSetIfChanged(ref _destinationBranch, value); }
        }

        public string Message
        {
            get { return _message; }
            set { this.RaiseAndSetIfChanged(ref _message, value); }
        }

        public string Description
        {
            get { return _description; }
            set { this.RaiseAndSetIfChanged(ref _description, value); }
        }

        [Required]
        public string Title
        {
            get { return _Title; }
            set { this.RaiseAndSetIfChanged(ref _Title, value); }
        }

        [Required]
        public bool CloseSourceBranch
        {
            get { return _closeSourceBranch; }
            set { this.RaiseAndSetIfChanged(ref _closeSourceBranch, value); }
        }

        public IEnumerable<IReactiveCommand> ThrowableCommands => new[] { _initializeCommand, _createNewPullRequestCommand };
        public IEnumerable<IReactiveCommand> LoadingCommands => new[] { _initializeCommand, _createNewPullRequestCommand };
        public string GitClientType => _gitClientService.GitClientType;

        public bool IsLoading
        {
            get { return _isLoading; }
            set { this.RaiseAndSetIfChanged(ref _isLoading, value); }
        }

        public ICommand InitializeCommand => _initializeCommand;
        public ICommand CreateNewPullRequestCommand => _createNewPullRequestCommand;

        [ImportingConstructor]
        public CreatePullRequestsViewModel(
            IGitClientService gitClientService,
            IGitService gitService,
            IPageNavigationService<IPullRequestsWindow> pageNavigationService,
            IEventAggregatorService eventAggregator
            )
        {
            _gitClientService = gitClientService;
            _gitService = gitService;
            _pageNavigationService = pageNavigationService;
            _eventAggregator = eventAggregator;
            CloseSourceBranch = false;
            SetupObservables();
        }

        private void SetupObservables()
        {
            _eventAggregator.GetEvent<ActiveRepositoryChangedEvent>()
                .SelectMany(async _ =>
                {
                    return await _initializeCommand.ExecuteAsync();
                })
                .Subscribe();

            this.WhenAnyValue(x => x.SourceBranch)
                .Where(x => x != null)
                .Subscribe(_ =>
                {
                    if (string.IsNullOrEmpty(SourceBranch.TrackedBranchName))
                        Message = $"Warning! Selected branch {SourceBranch.Name} is not a remote branch.";
                    else
                    {
                        var remoteBranch = RemoteBranches.FirstOrDefault(x => SourceBranch.TrackedBranchName == x.Name);
                        if (remoteBranch?.Target.Hash != SourceBranch.Target.Hash)
                            Message = $"Warning! Selected branch {SourceBranch.Name} is out of sync with a remote branch.";
                        else
                            Message = string.Empty;
                    }
                });
        }

        public void InitializeCommands()
        {
            _initializeCommand = ReactiveCommand.CreateAsyncTask(CanLoadPullRequests(), _ => LoadBranches());
            _createNewPullRequestCommand = ReactiveCommand.CreateAsyncTask(CanCreatePullRequest(), _ => CreateNewPullRequest());

            _createNewPullRequestCommand.Subscribe(_ =>
            {
                _pageNavigationService.NavigateBack(true);
            });
        }


        private async Task CreateNewPullRequest()
        {
            var gitPullRequest = new GitPullRequest(Title, Description, SourceBranch.TrackedBranchName, DestinationBranch.Name)
            {
                CloseSourceBranch = CloseSourceBranch
            };
            await _gitClientService.CreatePullRequest(gitPullRequest, _currentRepo.Name, _currentRepo.Owner);
        }

        private async Task LoadBranches()
        {
            _currentRepo = _gitService.GetActiveRepository();

            LocalBranches = _currentRepo.Branches.Where(x => !x.IsRemote).ToList();
            RemoteBranches = (await _gitClientService.GetBranches(_currentRepo.Name, _currentRepo.Owner)).OrderBy(x => x.Name).ToList();

            SourceBranch = LocalBranches.FirstOrDefault(x => x.IsHead);
            DestinationBranch = RemoteBranches.FirstOrDefault(x => x.IsDefault) ?? RemoteBranches.FirstOrDefault(x => x.Name != SourceBranch.Name);
        }

        private IObservable<bool> CanLoadPullRequests()
        {
            return Observable.Return(true);
        }

        private IObservable<bool> CanCreatePullRequest()
        {
            return ValidationObservable.Select(x => CanExecute()).StartWith(CanExecute());
        }

        private bool CanExecute()
        {
            return IsObjectValid() &&
                   !string.IsNullOrEmpty(SourceBranch?.Name) &&
                   !string.IsNullOrEmpty(SourceBranch?.TrackedBranchName) &&
                   !string.IsNullOrEmpty(DestinationBranch?.Name) &&
                   ValidateBranches();
        }

        public bool ValidateBranches()
        {
            return DestinationBranch?.Name != SourceBranch?.TrackedBranchName;
        }
    }
}
