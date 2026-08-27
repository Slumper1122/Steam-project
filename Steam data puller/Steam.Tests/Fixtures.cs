namespace Steam.Tests;

/// <summary>Realistic Steam API JSON responses for mocking.</summary>
internal static class Fixtures
{
    public const int AppId = 264710;

    public static string AppDetails(int appId = AppId) =>
        $@"{{
          ""{appId}"": {{
            ""success"": true,
            ""data"": {{
              ""type"": ""game"",
              ""name"": ""Subnautica"",
              ""steam_appid"": {appId},
              ""is_free"": false,
              ""short_description"": ""Descend into the depths of an alien underwater world."",
              ""developers"": [""Unknown Worlds Entertainment""],
              ""publishers"": [""Unknown Worlds Entertainment""],
              ""price_overview"": {{
                ""currency"": ""USD"",
                ""initial"": 2999,
                ""final"": 2999,
                ""discount_percent"": 0,
                ""initial_formatted"": ""$29.99"",
                ""final_formatted"": ""$29.99""
              }},
              ""categories"": [{{""id"": 2, ""description"": ""Single-player""}}],
              ""genres"": [{{""id"": ""1"", ""description"": ""Action""}}],
              ""release_date"": {{""coming_soon"": false, ""date"": ""Jan 23, 2018""}},
              ""dlc"": [2012840]
            }}
          }}
        }}";

    public static string CurrentPlayers(int count = 3340) =>
        $@"{{""response"":{{""player_count"":{count},""result"":1}}}}";

    public static string ReviewSummary(int positive = 120000, int negative = 3000) =>
        $@"{{
          ""success"": 1,
          ""query_summary"": {{
            ""review_score"": 9,
            ""review_score_desc"": ""Overwhelmingly Positive"",
            ""total_positive"": {positive},
            ""total_negative"": {negative},
            ""total_reviews"": {positive + negative}
          }}
        }}";

    public const string News = @"{
          ""appnews"": {
            ""appid"": 264710,
            ""newsitems"": [
              {
                ""gid"": ""1"",
                ""title"": ""Update 1.0"",
                ""url"": ""https://store.steampowered.com/news/264710"",
                ""date"": 1700000000,
                ""author"": ""dev"",
                ""feedlabel"": ""Patch Notes"",
                ""feedname"": ""steam_community_announcements"",
                ""contents"": ""Content here""
              }
            ]
          }
        }";

    public const string Achievements = @"{
          ""achievementpercentages"": {
            ""achievements"": [
              {""name"": ""DiveForTheVeryFirstTime"", ""percent"": 89.7},
              {""name"": ""BuildBase"", ""percent"": 53.3}
            ]
          }
        }";

    public const string SteamSpy = @"{
          ""appid"": 264710,
          ""name"": ""Subnautica"",
          ""developer"": ""Unknown Worlds Entertainment"",
          ""publisher"": ""Unknown Worlds Entertainment"",
          ""owners"": ""5,000,000 .. 10,000,000"",
          ""average_forever"": 0,
          ""median_forever"": 0,
          ""average_2weeks"": 0,
          ""median_2weeks"": 0,
          ""price"": ""2999"",
          ""initialprice"": ""2999"",
          ""discount"": ""0"",
          ""ccu"": 2719,
          ""tags"": {""Underwater"": 5000, ""Survival"": 4000, ""Open World"": 3000}
        }";
}
