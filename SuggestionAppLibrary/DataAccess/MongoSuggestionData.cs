using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess;

public class MongoSuggestionData : ISuggestionData
{
   private readonly IDbConnection _db;
   private readonly IUserData _userData;
   private readonly IMemoryCache _cache;
   private readonly IMongoCollection<SuggestionModel> _suggestions;
   private const string CacheName = "SuggestionData";

   public MongoSuggestionData(IDbConnection db, IUserData userData, IMemoryCache cache)
   {
      _db = db;
      _userData = userData;
      _cache = cache;
   }

   public async Task<List<SuggestionModel>> GetSuggestionAsync()
   {
      var output = _cache.Get<List<SuggestionModel>>(CacheName);
      if (output == null)
      {
         var result = await _suggestions.FindAsync(s => s.Archived == false);
         output = result.ToList();
         _cache.Set(CacheName, result, TimeSpan.FromMinutes(1));
      }

      return output;
   }

   public async Task<List<SuggestionModel>> GetApprovedSuggestions()
   {
      var output = await GetSuggestionAsync();
      return output.Where(s => s.ApprovedForRelease).ToList();
   }

   public async Task<SuggestionModel> GetSuggestionAsync(string suggestionId)
   {
      var result = await _suggestions.FindAsync(s => s.Id == suggestionId);
      return result.FirstOrDefault();
   }

   public async Task<List<SuggestionModel>> GetSuggestionsWaitingForApproval()
   {
      var output = await GetSuggestionAsync();
      return output.Where(s =>
         s.ApprovedForRelease == false
         && s.Rejected == false).ToList();
   }

   public async Task UpdateSuggestion(SuggestionModel suggestion)
   {
      await _suggestions.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);
      _cache.Remove(CacheName);
   }

   public async Task UpvoteSuggestion(string suggestionId, string userId)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionsInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         var suggestion = (await suggestionsInTransaction.FindAsync(s => s.Id == suggestionId)).First();

         bool isUpvoted = suggestion.UserVotes.Add(userId);
         if (isUpvoted == false)
         {
            suggestion.UserVotes.Remove(userId);
         }

         await suggestionsInTransaction.ReplaceOneAsync(s => s.Id == suggestionId, suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUserAsync(userId);
         if (isUpvoted)
         {
            user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
         }
         else
         {
            var suggestionToRemove = user.VotedOnSuggestions.Where(s => s.Id == suggestionId).First();
            user.VotedOnSuggestions.Remove(suggestionToRemove);
         }
         await usersInTransaction.ReplaceOneAsync(u => u.Id == userId, user);

         await session.CommitTransactionAsync();
         _cache.Remove(CacheName);
      }
      catch (Exception)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }

   public async Task CreateSuggestionModel(SuggestionModel suggestion)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionsInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         await suggestionsInTransaction.InsertOneAsync(suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUserAsync(suggestion.Author.Id);
         user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
         await usersInTransaction.ReplaceOneAsync(u => u.Id == user.Id, user);

         await session.CommitTransactionAsync();
      }
      catch (Exception)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }
}
