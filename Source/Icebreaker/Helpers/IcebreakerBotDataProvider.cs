//----------------------------------------------------------------------------------------------
// <copyright file="IcebreakerBotDataProvider.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>
//----------------------------------------------------------------------------------------------

namespace Icebreaker.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Icebreaker.Interfaces;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.Azure;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Azure.Documents.Linq;
    using Microsoft.Bot.Schema;

    /// <summary>
    /// Data provider routines
    /// </summary>
    public class IcebreakerBotDataProvider : IBotDataProvider
    {
        // Request the minimum throughput by default
        private const int DefaultRequestThroughput = 400;

        private readonly TelemetryClient telemetryClient;
        private readonly Lazy<Task> initializeTask;
        private readonly ISecretsHelper secretsHelper;
        private DocumentClient documentClient;
        private Database database;
        private DocumentCollection pairsCollection;
        private DocumentCollection teamsCollection;
        private DocumentCollection usersCollection;

        /// <summary>
        /// Initializes a new instance of the <see cref="IcebreakerBotDataProvider"/> class.
        /// </summary>
        /// <param name="telemetryClient">The telemetry client to use</param>
        /// <param name="secretsHelper">Secrets helper to fetch secrets</param>
        public IcebreakerBotDataProvider(TelemetryClient telemetryClient, ISecretsHelper secretsHelper)
        {
            this.telemetryClient = telemetryClient;
            this.secretsHelper = secretsHelper;
            this.initializeTask = new Lazy<Task>(() => this.InitializeAsync());
        }

        /// <summary>
        /// Updates team installation status in store. If the bot is installed, the info is saved, otherwise info for the team is deleted.
        /// </summary>
        /// <param name="team">The team installation info</param>
        /// <param name="installed">Value that indicates if bot is installed</param>
        /// <returns>Tracking task</returns>
        public async Task UpdateTeamInstallStatusAsync(TeamInstallInfo team, bool installed)
        {
            await this.EnsureInitializedAsync();

            if (installed)
            {
                var response = await this.documentClient.UpsertDocumentAsync(this.teamsCollection.SelfLink, team);
            }
            else
            {
                var documentUri = UriFactory.CreateDocumentUri(this.database.Id, this.teamsCollection.Id, team.Id);
                var response = await this.documentClient.DeleteDocumentAsync(documentUri, new RequestOptions { PartitionKey = new PartitionKey(team.Id) });
            }
        }

        /// <summary>
        /// Get a list of past pairings
        /// </summary>
        /// <returns>List of past pairings.</returns>
        public async Task<IList<PairInfo>> GetPairHistoryAsync()
        {
            await this.EnsureInitializedAsync();

            var pairHistory = new List<PairInfo>();

            try
            {
                using (var lookupQuery = this.documentClient
                    .CreateDocumentQuery<PairInfo>(this.pairsCollection.SelfLink, new FeedOptions { EnableCrossPartitionQuery = true })
                    .AsDocumentQuery())
                {
                    while (lookupQuery.HasMoreResults)
                    {
                        var response = await lookupQuery.ExecuteNextAsync<PairInfo>();
                        pairHistory.AddRange(response);
                    }
                }
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
            }

            return pairHistory;
        }

        /// <summary>
        /// Record a pairing that was made
        /// </summary>
        /// <param name="pair">A pairing.</param>
        /// <param name="iteration">Value that indicates the iteration cycle when the pairing happened.</param>
        /// <returns>Tracking task</returns>
        public async Task AddPairAsync(Tuple<string, string> pair, int iteration)
        {
            await this.EnsureInitializedAsync();

            var pairInfo = new PairInfo
            {
                User1Id = pair.Item1,
                User2Id = pair.Item2,
                Iteration = iteration,
            };

            var response = await this.documentClient.CreateDocumentAsync(this.pairsCollection.SelfLink, pairInfo);
        }

        /// <summary>
        /// Get the list of teams to which the app was installed.
        /// </summary>
        /// <returns>List of installed teams</returns>
        public async Task<IList<TeamInstallInfo>> GetInstalledTeamsAsync()
        {
            await this.EnsureInitializedAsync();

            var installedTeams = new List<TeamInstallInfo>();

            try
            {
                using (var lookupQuery = this.documentClient
                    .CreateDocumentQuery<TeamInstallInfo>(this.teamsCollection.SelfLink, new FeedOptions { EnableCrossPartitionQuery = true })
                    .AsDocumentQuery())
                {
                    while (lookupQuery.HasMoreResults)
                    {
                        var response = await lookupQuery.ExecuteNextAsync<TeamInstallInfo>();
                        installedTeams.AddRange(response);
                    }
                }
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
            }

            return installedTeams;
        }

        /// <summary>
        /// Returns the team that the bot has been installed to
        /// </summary>
        /// <param name="teamId">The team id</param>
        /// <returns>Team that the bot is installed to</returns>
        public async Task<TeamInstallInfo> GetInstalledTeamAsync(string teamId)
        {
            await this.EnsureInitializedAsync();

            // Get team install info
            try
            {
                var documentUri = UriFactory.CreateDocumentUri(this.database.Id, this.teamsCollection.Id, teamId);
                return await this.documentClient.ReadDocumentAsync<TeamInstallInfo>(documentUri, new RequestOptions { PartitionKey = new PartitionKey(teamId) });
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
                return null;
            }
        }

        /// <summary>
        /// Get the stored information about the given user
        /// </summary>
        /// <param name="userId">User id</param>
        /// <returns>User information</returns>
        public async Task<UserInfo> GetUserInfoAsync(string userId)
        {
            await this.EnsureInitializedAsync();

            try
            {
                var documentUri = UriFactory.CreateDocumentUri(this.database.Id, this.usersCollection.Id, userId);
                return await this.documentClient.ReadDocumentAsync<UserInfo>(documentUri, new RequestOptions { PartitionKey = new PartitionKey(userId) });
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
                return null;
            }
        }

        /// <summary>
        /// Get the stored information about given users
        /// </summary>
        /// <returns>User information</returns>
        public async Task<Dictionary<string, IDictionary<string, bool>>> GetAllUsersOptInStatusAsync()
        {
            await this.EnsureInitializedAsync();

            try
            {
                var collectionLink = UriFactory.CreateDocumentCollectionUri(this.database.Id, this.usersCollection.Id);
                var query = this.documentClient.CreateDocumentQuery<UserInfo>(
                        collectionLink,
#pragma warning disable SA1118 // Parameter must not span multiple lines
                        new FeedOptions
                        {
                            EnableCrossPartitionQuery = true,

                            // Fetch items in bulk according to DB engine capability
                            MaxItemCount = -1,

                            // Max partition to query at a time
                            MaxDegreeOfParallelism = -1
                        })
#pragma warning restore SA1118 // Parameter must not span multiple lines
                    .Select(u => new UserInfo { Id = u.Id, OptedIn = u.OptedIn })
                    .AsDocumentQuery();
                var usersOptInStatusLookup = new Dictionary<string, IDictionary<string, bool>>();
                while (query.HasMoreResults)
                {
                    // Note that ExecuteNextAsync can return many records in each call
                    var responseBatch = await query.ExecuteNextAsync<UserInfo>();
                    foreach (var userInfo in responseBatch)
                    {
                        usersOptInStatusLookup.Add(userInfo.Id, userInfo.OptedIn);
                    }
                }

                return usersOptInStatusLookup;
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
                return null;
            }
        }

        /// <summary>
        /// Get the stored profiles of given users
        /// </summary>
        /// <returns>User's custom profiles</returns>
        public async Task<Dictionary<string, string>> GetAllUsersProfileAsync()
        {
            await this.EnsureInitializedAsync();

            try
            {
                var collectionLink = UriFactory.CreateDocumentCollectionUri(this.database.Id, this.usersCollection.Id);
                var query = this.documentClient.CreateDocumentQuery<UserInfo>(
                        collectionLink,
#pragma warning disable SA1118 // Parameter must not span multiple lines
                        new FeedOptions
                        {
                            EnableCrossPartitionQuery = true,

                            // Fetch items in bulk according to DB engine capability
                            MaxItemCount = -1,

                            // Max partition to query at a time
                            MaxDegreeOfParallelism = -1
                        })
#pragma warning restore SA1118 // Parameter must not span multiple lines
                    .Select(u => new UserInfo { Id = u.Id, Profile = u.Profile })
                    .AsDocumentQuery();
                var usersProfileLookup = new Dictionary<string, string>();
                while (query.HasMoreResults)
                {
                    // Note that ExecuteNextAsync can return many records in each call
                    var responseBatch = await query.ExecuteNextAsync<UserInfo>();
                    foreach (var userInfo in responseBatch)
                    {
                        usersProfileLookup.Add(userInfo.Id, userInfo.Profile);
                    }
                }

                return usersProfileLookup;
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackException(ex.InnerException);
                return null;
            }
        }

        /// <summary>
        /// Sets the profile for the given user
        /// </summary>
        /// <param name="userId">User id</param>
        /// <param name="profile">User's desired profile</param>
        /// <returns>Tracking task</returns>
        public async Task SetUserProfileAsync(string userId, string profile)
        {
            await this.EnsureInitializedAsync();

            var user = await this.GetUserInfoAsync(userId);

            var userInfo = new UserInfo
            {
                TenantId = user.TenantId,
                UserId = user.UserId,
                OptedIn = user.OptedIn,
                ServiceUrl = user.ServiceUrl,
                Profile = profile
            };

            await this.documentClient.UpsertDocumentAsync(this.usersCollection.SelfLink, userInfo);
        }

        /// <summary>
        /// Set the user info for the given user
        /// </summary>
        /// <param name="tenantId">Tenant id</param>
        /// <param name="userId">User id</param>
        /// <param name="optedIn">User opt-in status for each team user is in</param>
        /// <param name="serviceUrl">User service URL</param>
        /// <returns>Tracking task</returns>
        public async Task SetUserInfoAsync(string tenantId, string userId, IDictionary<string, bool> optedIn, string serviceUrl)
        {
            await this.EnsureInitializedAsync();

            var userInfo = new UserInfo
            {
                TenantId = tenantId,
                UserId = userId,
                OptedIn = optedIn,
                ServiceUrl = serviceUrl
            };
            await this.documentClient.UpsertDocumentAsync(this.usersCollection.SelfLink, userInfo);
        }

        /// <summary>
        /// Add team to user's teams
        /// </summary>
        /// <param name="tenantId">Tenant id</param>
        /// <param name="userId">User id</param>
        /// <param name="teamId">Team to add</param>
        /// <param name="serviceUrl">User service URL</param>
        /// <returns>Tracking task</returns>
        public async Task AddUserTeamAsync(string tenantId, string userId, string teamId, string serviceUrl)
        {
            await this.EnsureInitializedAsync();

            // create document if user info doesn't exist yet, otherwise update existing document
            var userInfo = await this.GetUserInfoAsync(userId);
            var optedIn = userInfo?.OptedIn ?? new Dictionary<string, bool>();
            optedIn.Add(teamId, true);

            await this.SetUserInfoAsync(tenantId, userId, optedIn, serviceUrl);
        }

        /// <summary>
        /// Remove team from user's teams
        /// </summary>
        /// <param name="userId">User id</param>
        /// <param name="teamId">Team to remove</param>
        /// <returns>Tracking task</returns>
        public async Task RemoveUserTeamAsync(string userId, string teamId)
        {
            await this.EnsureInitializedAsync();

            // create document if user info doesn't exist yet, otherwise update existing document
            var userInfo = await this.GetUserInfoAsync(userId);
            var optedIn = userInfo.OptedIn;
            optedIn.Remove(teamId);

            await this.SetUserInfoAsync(userInfo.TenantId, userId, optedIn, userInfo.ServiceUrl);
        }

        /// <summary>
        /// Initializes the database connection.
        /// </summary>
        /// <returns>Tracking task</returns>
        private async Task InitializeAsync()
        {
            this.telemetryClient.TrackTrace("Initializing data store");

            var endpointUrl = CloudConfigurationManager.GetSetting("CosmosDBEndpointUrl");
            var databaseName = CloudConfigurationManager.GetSetting("CosmosDBDatabaseName");
            var pairsCollectionName = CloudConfigurationManager.GetSetting("CosmosCollectionPairs");
            var teamsCollectionName = CloudConfigurationManager.GetSetting("CosmosCollectionTeams");
            var usersCollectionName = CloudConfigurationManager.GetSetting("CosmosCollectionUsers");

            this.documentClient = new DocumentClient(new Uri(endpointUrl), this.secretsHelper.CosmosDBKey);

            var requestOptions = new RequestOptions { OfferThroughput = DefaultRequestThroughput };
            bool useSharedOffer = true;

            // Create the database if needed
            try
            {
                this.database = await this.documentClient.CreateDatabaseIfNotExistsAsync(new Database { Id = databaseName }, requestOptions);
            }
            catch (DocumentClientException ex)
            {
                if (ex.Error?.Message?.Contains("SharedOffer is Disabled") ?? false)
                {
                    this.telemetryClient.TrackTrace("Database shared offer is disabled for the account, will provision throughput at container level", SeverityLevel.Information);
                    useSharedOffer = false;

                    this.database = await this.documentClient.CreateDatabaseIfNotExistsAsync(new Database { Id = databaseName });
                }
                else
                {
                    throw;
                }
            }

            // Get a reference to the Pairs collection, creating it if needed
            var pairCollectionDefinition = new DocumentCollection
            {
                Id = pairsCollectionName,
            };
            pairCollectionDefinition.PartitionKey.Paths.Add("/id");
            this.pairsCollection = await this.documentClient.CreateDocumentCollectionIfNotExistsAsync(this.database.SelfLink, pairCollectionDefinition, useSharedOffer ? null : requestOptions);

            // Get a reference to the Teams collection, creating it if needed
            var teamsCollectionDefinition = new DocumentCollection
            {
                Id = teamsCollectionName,
            };
            teamsCollectionDefinition.PartitionKey.Paths.Add("/id");
            this.teamsCollection = await this.documentClient.CreateDocumentCollectionIfNotExistsAsync(this.database.SelfLink, teamsCollectionDefinition, useSharedOffer ? null : requestOptions);

            // Get a reference to the Users collection, creating it if needed
            var usersCollectionDefinition = new DocumentCollection
            {
                Id = usersCollectionName
            };
            usersCollectionDefinition.PartitionKey.Paths.Add("/id");
            this.usersCollection = await this.documentClient.CreateDocumentCollectionIfNotExistsAsync(this.database.SelfLink, usersCollectionDefinition, useSharedOffer ? null : requestOptions);

            this.telemetryClient.TrackTrace("Data store initialized");
        }

        private async Task EnsureInitializedAsync()
        {
            await this.initializeTask.Value;
        }
    }
}