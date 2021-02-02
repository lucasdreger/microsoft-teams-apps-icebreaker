﻿// <copyright file="IBotDataProvider.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Icebreaker.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Icebreaker.Helpers;
    using Microsoft.Bot.Schema;

    /// <summary>
    /// Data provider routines
    /// </summary>
    public interface IBotDataProvider
    {
        /// <summary>
        /// Get the list of teams to which the app was installed.
        /// </summary>
        /// <returns>List of installed teams</returns>
        Task<IList<TeamInstallInfo>> GetInstalledTeamsAsync();

        /// <summary>
        /// Get the stored information about given users
        /// </summary>
        /// <returns>User information</returns>
        Task<Dictionary<string, IDictionary<string, bool>>> GetAllUsersOptInStatusAsync();

        /// <summary>
        /// Get the stored profiles of given users
        /// </summary>
        /// <returns>User's custom profiles</returns>
        Task<Dictionary<string, string>> GetAllUsersProfileAsync();

        /// <summary>
        /// Returns the team that the bot has been installed to
        /// </summary>
        /// <param name="teamId">The team id</param>
        /// <returns>Team that the bot is installed to</returns>
        Task<TeamInstallInfo> GetInstalledTeamAsync(string teamId);

        /// <summary>
        /// Updates team installation status in store. If the bot is installed, the info is saved, otherwise info for the team is deleted.
        /// </summary>
        /// <param name="team">The team installation info</param>
        /// <param name="installed">Value that indicates if bot is installed</param>
        /// <returns>Tracking task</returns>
        Task UpdateTeamInstallStatusAsync(TeamInstallInfo team, bool installed);

        /// <summary>
        /// Get the stored information about the given user
        /// </summary>
        /// <param name="userId">User id</param>
        /// <returns>User information</returns>
        Task<UserInfo> GetUserInfoAsync(string userId);

        /// <summary>
        /// Set the user info for the given user
        /// </summary>
        /// <param name="tenantId">Tenant id</param>
        /// <param name="userId">User id</param>
        /// <param name="optedIn">User opt-in status for each team user is in</param>
        /// <param name="serviceUrl">User service URL</param>
        /// <returns>Tracking task</returns>
        Task SetUserInfoAsync(string tenantId, string userId, IDictionary<string, bool> optedIn, string serviceUrl);

        /// <summary>
        /// Add team to user's teams
        /// </summary>
        /// <param name="tenantId">Tenant id</param>
        /// <param name="userId">User id</param>
        /// <param name="teamId">Team to add</param>
        /// <param name="serviceUrl">User service URL</param>
        /// <returns>Tracking task</returns>
        Task AddUserTeamAsync(string tenantId, string userId, string teamId, string serviceUrl);

        /// <summary>
        /// Remove team from user's teams
        /// </summary>
        /// <param name="userId">User id</param>
        /// <param name="teamId">Team to remove</param>
        /// <returns>Tracking task</returns>
        Task RemoveUserTeamAsync(string userId, string teamId);

       /// <summary>
        /// Sets the profile for the given user
        /// </summary>
        /// <param name="userId">User id</param>
        /// <param name="profile">User's desired profile</param>
        /// <returns>Tracking task</returns>
        Task SetUserProfileAsync(string userId, string profile);

        /// <summary>
        /// Get a list of past pairings
        /// </summary>
        /// <returns>List of past pairings.</returns>
        Task<IList<PairInfo>> GetPairHistoryAsync();

        /// <summary>
        /// Record a pairing that was made
        /// </summary>
        /// <param name="pair">A pairing.</param>
        /// <param name="iteration">Value that indicates the iteration cycle when the pairing happened.</param>
        /// <returns>Tracking task</returns>
        Task AddPairAsync(Tuple<string, string> pair, int iteration);
    }
}