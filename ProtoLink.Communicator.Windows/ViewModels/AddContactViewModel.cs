using System.Net.Http;
using System.Net.Http.Json;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ProtoLink.Communicator.Windows.Core;
using ProtoLink.Communicator.Windows.Models;
using ProtoLink.Communicator.Windows.Services;

namespace ProtoLink.Communicator.Windows.ViewModels;

public class AddContactViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private string _userId = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isLoading;
    private readonly bool _isEmbedded;

    public AddContactViewModel(IAuthService authService, HttpClient httpClient, ILogger logger, bool isEmbedded = false)
    {
        _authService = authService;
        _httpClient = httpClient;
        _logger = logger;
        _isEmbedded = isEmbedded;
        AddContactCommand = new RelayCommand(async _ => await AddContactAsync(), _ => !string.IsNullOrWhiteSpace(UserId) && !_isLoading);
        CloseCommand = new RelayCommand(_ => OnCloseRequested?.Invoke());
    }

    public string UserId { get => _userId; set { _userId = value; OnPropertyChanged(); } }
    public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public bool IsEmbedded => _isEmbedded;
    public ICommand AddContactCommand { get; }
    public ICommand CloseCommand { get; }
    public event Action? OnContactAdded;
    public event Action? OnCloseRequested;

    private async Task AddContactAsync()
    {
        if (_authService.CurrentToken == null) return;
        IsLoading = true;
        StatusMessage = "Adding contact...";
        try
        {
            if (!Guid.TryParse(UserId.Trim(), out var contactUserId))
            {
                StatusMessage = "Invalid user ID format.";
                return;
            }
            if (contactUserId == _authService.CurrentToken.UserId)
            {
                StatusMessage = "You cannot add yourself.";
                return;
            }

            var userId = _authService.CurrentToken.UserId;
            var response = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { SystemEntities.Contacts, userId }
            });
            var results = await response.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            var userContacts = results?.FirstOrDefault();
            Guid? userContactsId;

            if (userContacts == null)
            {
                var addResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
                {
                    Code = "UserContacts",
                    ParentIds = new[] { SystemEntities.Contacts, userId }
                });
                if (!addResponse.IsSuccessStatusCode) { StatusMessage = "Failed to create contacts."; return; }
                var addResult = await addResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                userContactsId = addResult.GetProperty("id").GetGuid();
            }
            else
            {
                userContactsId = userContacts.Id;
            }

            if (!userContactsId.HasValue) { StatusMessage = "Failed."; return; }

            var existingResponse = await _httpClient.PostAsJsonAsync("api/Entities/GetEntities", new GetEntitiesContract
            {
                ParentIds = new[] { userContactsId.Value, contactUserId },
                IncludeValues = true
            });
            var existing = await existingResponse.Content.ReadFromJsonAsync<List<GetEntitiesResult>>();
            if (existing?.Any() == true)
            {
                StatusMessage = "Contact already exists.";
                OnContactAdded?.Invoke();
                return;
            }

            var addContactResponse = await _httpClient.PostAsJsonAsync("api/Entities/AddEntity", new AddEntityContract
            {
                Code = contactUserId.ToString(),
                ParentIds = new[] { userContactsId.Value, contactUserId },
                Values = new List<AddValueContract>
                {
                    new() { Type = TypeOfValue.String, Value = $"userid:{contactUserId}", ParentIds = Array.Empty<Guid>() }
                }
            });

            if (addContactResponse.IsSuccessStatusCode)
            {
                StatusMessage = "Contact added.";
                UserId = string.Empty;
                OnContactAdded?.Invoke();
            }
            else
            {
                StatusMessage = "Failed to add contact.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add contact error");
            StatusMessage = "An error occurred.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
