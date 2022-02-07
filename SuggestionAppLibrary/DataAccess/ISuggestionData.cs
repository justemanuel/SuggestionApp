
namespace SuggestionAppLibrary.DataAccess;

public interface ISuggestionData
{
   Task CreateSuggestionModel(SuggestionModel suggestion);
   Task<List<SuggestionModel>> GetApprovedSuggestions();
   Task<List<SuggestionModel>> GetSuggestionAsync();
   Task<SuggestionModel> GetSuggestionAsync(string suggestionId);
   Task<List<SuggestionModel>> GetSuggestionsWaitingForApproval();
   Task UpdateSuggestion(SuggestionModel suggestion);
   Task UpvoteSuggestion(string suggestionId, string userId);
}