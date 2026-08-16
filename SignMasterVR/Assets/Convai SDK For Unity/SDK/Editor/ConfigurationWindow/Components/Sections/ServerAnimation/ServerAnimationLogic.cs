#if CONVAI_ENABLE_SERVER_ANIMATION
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Editor.ConfigurationWindow.Components;
using Convai.RestAPI;
using Convai.RestAPI.Internal;
using Convai.Runtime;
using Convai.Runtime.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Convai.Editor.ConfigurationWindow.Components.Sections.ServerAnimation
{
    /// <summary>
    /// Handles server animation list and import logic.
    /// </summary>
    public class ServerAnimationLogic : IDisposable
    {
        private readonly List<ConvaiServerAnimationItem> _animations = new();
        private readonly Dictionary<string, Texture2D> _animationThumbnails = new();
        private readonly List<ServerAnimationItemResponse> _selectedAnimations = new();
        private readonly ConfigurationWindowContext _context;
        private readonly ConvaiServerAnimationSection _ui;

        private bool _isDisposed;
        private bool _isSectionVisible;
        private bool _isRefreshing;
        private int _requestVersion;
        private int _currentPage = 1;
        private ServerAnimationListResponse _response;
        private string _apiKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerAnimationLogic"/> class.
        /// </summary>
        /// <param name="section">UI section instance to bind to.</param>
        /// <param name="context">Shared window context.</param>
        public ServerAnimationLogic(ConvaiServerAnimationSection section, ConfigurationWindowContext context)
        {
            _ui = section;
            _context = context;

            _ui.RefreshButton.clicked += RefreshAnimationList;
            _ui.ImportButton.clicked += ImportButtonOnClicked;
            _ui.PreviousButton.clicked += PreviousButtonOnClicked;
            _ui.NextButton.clicked += NextButtonOnClicked;

            if (_context != null)
            {
                _context.ApiKeyAvailabilityChanged += OnApiKeyAvailabilityChanged;
            }
        }

        /// <summary>
        /// Sets whether this section is visible.
        /// </summary>
        public void SetSectionVisible(bool isVisible)
        {
            _isSectionVisible = isVisible;
            if (!_isSectionVisible)
            {
                CancelPendingRequests();
                return;
            }

            RefreshAnimationList();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            CancelPendingRequests();

            _ui.RefreshButton.clicked -= RefreshAnimationList;
            _ui.ImportButton.clicked -= ImportButtonOnClicked;
            _ui.PreviousButton.clicked -= PreviousButtonOnClicked;
            _ui.NextButton.clicked -= NextButtonOnClicked;

            if (_context != null)
            {
                _context.ApiKeyAvailabilityChanged -= OnApiKeyAvailabilityChanged;
            }
        }

        private void OnApiKeyAvailabilityChanged(bool hasApiKey)
        {
            if (!_isSectionVisible)
            {
                return;
            }

            CancelPendingRequests();
            if (hasApiKey)
            {
                RefreshAnimationList();
            }
            else
            {
                _ui.AnimationContainer.contentContainer.Clear();
            }
        }

        private void CancelPendingRequests()
        {
            _requestVersion++;
            _isRefreshing = false;
        }

        private void NextButtonOnClicked()
        {
            _currentPage++;
            RefreshAnimationList();
        }

        private void PreviousButtonOnClicked()
        {
            _currentPage--;
            RefreshAnimationList();
        }

        private void ImportButtonOnClicked()
        {
            DisableButtons();
            _animations.ForEach(animation => animation.CanBeSelected = false);
            ServerAnimationService.ImportAnimations(_selectedAnimations, OnImportComplete, message =>
            {
                ConvaiLogger.Error(message, LogCategory.REST);
                EditorUtility.DisplayDialog("Import Failed", message, "Ok");
                OnImportComplete();
            });
        }

        private void DisableButtons()
        {
            _ui.RefreshButton.SetEnabled(false);
            _ui.NextButton.SetEnabled(false);
            _ui.PreviousButton.SetEnabled(false);
            _ui.ImportButton.SetEnabled(false);
        }

        private void OnImportComplete()
        {
            _ui.RefreshButton.SetEnabled(true);
            _ui.NextButton.SetEnabled(_response != null && _currentPage != _response.TotalPages);
            _ui.PreviousButton.SetEnabled(_currentPage != 1);
            _ui.ImportButton.SetEnabled(false);
            _selectedAnimations.Clear();
            _animations.ForEach(animation =>
            {
                animation.CanBeSelected = true;
                animation.IsSelected = false;
            });
        }

        private void RefreshAnimationList()
        {
            _ = RefreshAnimationListAsync(++_requestVersion);
        }

        private async Task RefreshAnimationListAsync(int requestId)
        {
            if (_isDisposed || !_isSectionVisible || _isRefreshing)
            {
                return;
            }

            ConvaiSettings settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ConvaiLogger.Error("ConvaiSettings not found or API key not set.", LogCategory.Editor);
                return;
            }

            _apiKey = settings.ApiKey;
            _ui.AnimationContainer.contentContainer.Clear();
            _selectedAnimations.Clear();
            DisableButtons();
            _isRefreshing = true;

            try
            {
                ConvaiRestClientOptions options = ConvaiRestOptionsFactory.Create(settings.ApiKey);
                using ConvaiRestClient client = new ConvaiRestClient(options);
                ServerAnimationListResponse response = await client.Animations.GetListAsync(_currentPage, "success");

                if (ShouldIgnoreResult(requestId))
                {
                    return;
                }

                OnAnimationListReceived(response);
            }
            catch (Exception ex)
            {
                if (!ShouldIgnoreResult(requestId))
                {
                    ConvaiLogger.Error(ex.Message, LogCategory.REST);
                }
            }
            finally
            {
                if (requestId == _requestVersion)
                {
                    _isRefreshing = false;
                }
            }
        }

        private bool ShouldIgnoreResult(int requestId)
        {
            return _isDisposed || requestId != _requestVersion || !_isSectionVisible;
        }

        private void OnAnimationListReceived(ServerAnimationListResponse response)
        {
            _response = response;
            _currentPage = response.CurrentPage;
            _animations.Clear();
            _ui.RefreshButton.SetEnabled(true);
            _ui.NextButton.SetEnabled(_currentPage != response.TotalPages);
            _ui.PreviousButton.SetEnabled(_currentPage != 1);
            foreach (ServerAnimationItemResponse animation in response.Animations)
            {
                ConvaiServerAnimationItem item = new ConvaiServerAnimationItem(OnAnimationSelected, animation)
                {
                    Name = { text = animation.AnimationName }
                };
                _animations.Add(item);
                _ui.AnimationContainer.contentContainer.Add(item);
                if (_animationThumbnails.TryGetValue(animation.AnimationID, out Texture2D thumbnail))
                {
                    item.Thumbnail.style.backgroundImage = new StyleBackground(thumbnail);
                }
                else if (!string.IsNullOrEmpty(animation.ThumbnailURL))
                {
                    _ = DownloadThumbnailAsyncInternal(animation, item);
                }
            }
        }

        private async Task DownloadThumbnailAsyncInternal(ServerAnimationItemResponse animation, ConvaiServerAnimationItem item)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return;
            }

            try
            {
                ConvaiRestClientOptions options = ConvaiRestOptionsFactory.Create(_apiKey);
                using ConvaiRestClient client = new ConvaiRestClient(options);
                byte[] bytes = await client.DownloadFileAsync(animation.ThumbnailURL);
                Texture2D texture = new Texture2D(256, 256);
                texture.LoadImage(bytes);
                _animationThumbnails[animation.AnimationID] = texture;
                item.Thumbnail.style.backgroundImage = new StyleBackground(texture);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(ex.Message, LogCategory.REST);
            }
        }

        private void OnAnimationSelected(bool isSelected, ServerAnimationItemResponse id)
        {
            if (isSelected)
            {
                _selectedAnimations.Add(id);
            }
            else
            {
                _selectedAnimations.Remove(id);
            }

            _ui.ImportButton.SetEnabled(_selectedAnimations.Count > 0);
        }
    }
}
#endif
