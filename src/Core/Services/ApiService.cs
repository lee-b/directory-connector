﻿using Bit.Core.Enums;
using Bit.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Bit.Core.Services
{
    public class ApiService
    {
        private static ApiService _instance;

        private ApiService()
        {
            Client = new HttpClient();
        }

        public static ApiService Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new ApiService();
                }

                return _instance;
            }
        }

        protected HttpClient Client { get; private set; }

        public virtual async Task<ApiResult<TokenResponse>> PostTokenAsync(TokenRequest requestObj)
        {
            var requestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(string.Concat(SettingsService.Instance.IdentityBaseUrl, "/connect/token")),
                Content = new FormUrlEncodedContent(requestObj.ToIdentityTokenRequest())
            };

            try
            {
                var response = await Client.SendAsync(requestMessage).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if(!response.IsSuccessStatusCode)
                {
                    var errorResponse = JObject.Parse(responseContent);
                    if(errorResponse["TwoFactorProviders2"] != null)
                    {
                        return ApiResult<TokenResponse>.Success(new TokenResponse
                        {
                            TwoFactorProviders2 = errorResponse["TwoFactorProviders2"]
                                .ToObject<Dictionary<TwoFactorProviderType, Dictionary<string, object>>>()
                        }, response.StatusCode);
                    }

                    return await HandleErrorAsync<TokenResponse>(response).ConfigureAwait(false);
                }

                var responseObj = JsonConvert.DeserializeObject<TokenResponse>(responseContent);
                return ApiResult<TokenResponse>.Success(responseObj, response.StatusCode);
            }
            catch
            {
                return HandledWebException<TokenResponse>();
            }
        }

        public virtual async Task<ApiResult> PostImportAsync(ImportRequest requestObj)
        {
            var tokenStateResponse = await HandleTokenStateAsync();
            if(!tokenStateResponse.Succeeded)
            {
                return tokenStateResponse;
            }

            var stringContent = JsonConvert.SerializeObject(requestObj);
            var requestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(string.Concat(SettingsService.Instance.ApiBaseUrl, "/organizations/",
                    SettingsService.Instance.Organization.Id, "/import")),
                Content = new StringContent(stringContent, Encoding.UTF8, "application/json"),
            };

            requestMessage.Headers.Add("Authorization", $"Bearer3 {TokenService.Instance.AccessToken}");

            try
            {
                var response = await Client.SendAsync(requestMessage).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)
                {
                    return await HandleErrorAsync(response).ConfigureAwait(false);
                }

                return ApiResult.Success(response.StatusCode);
            }
            catch
            {
                return HandledWebException();
            }
        }

        public virtual async Task<ApiResult<ProfileResponse>> GetProfileAsync()
        {
            var tokenStateResponse = await HandleTokenStateAsync<ProfileResponse>();
            if(!tokenStateResponse.Succeeded)
            {
                return tokenStateResponse;
            }

            var requestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(string.Concat(SettingsService.Instance.ApiBaseUrl, "/accounts/profile")),
            };

            requestMessage.Headers.Add("Authorization", $"Bearer3 {TokenService.Instance.AccessToken}");

            try
            {
                var response = await Client.SendAsync(requestMessage).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)
                {
                    return await HandleErrorAsync<ProfileResponse>(response).ConfigureAwait(false);
                }

                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var responseObj = JsonConvert.DeserializeObject<ProfileResponse>(responseContent);
                return ApiResult<ProfileResponse>.Success(responseObj, response.StatusCode);
            }
            catch
            {
                return HandledWebException<ProfileResponse>();
            }
        }

        public virtual async Task<ApiResult> PostTwoFactorSendEmailLoginAsync(TwoFactorEmailRequest requestObj)
        {
            var stringContent = JsonConvert.SerializeObject(requestObj);

            var requestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(string.Concat(SettingsService.Instance.ApiBaseUrl, "/two-factor/send-email-login")),
                Content = new StringContent(stringContent, Encoding.UTF8, "application/json")
            };

            try
            {
                var response = await Client.SendAsync(requestMessage).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)
                {
                    return await HandleErrorAsync(response).ConfigureAwait(false);
                }

                return ApiResult.Success(response.StatusCode);
            }
            catch
            {
                return HandledWebException();
            }
        }

        protected ApiResult HandledWebException()
        {
            return ApiResult.Failed(HttpStatusCode.BadGateway,
                new ApiError { Message = "There is a problem connecting to the server." });
        }

        protected ApiResult<T> HandledWebException<T>()
        {
            return ApiResult<T>.Failed(HttpStatusCode.BadGateway,
                new ApiError { Message = "There is a problem connecting to the server." });
        }

        protected async Task<ApiResult> HandleTokenStateAsync()
        {
            return await HandleTokenStateAsync(
                () => ApiResult.Success(HttpStatusCode.OK),
                () => HandledWebException(),
                (r) => HandleErrorAsync(r));
        }

        protected async Task<ApiResult<T>> HandleTokenStateAsync<T>()
        {
            return await HandleTokenStateAsync(
                () => ApiResult<T>.Success(default(T), HttpStatusCode.OK),
                () => HandledWebException<T>(),
                (r) => HandleErrorAsync<T>(r));
        }

        private async Task<T> HandleTokenStateAsync<T>(Func<T> success, Func<T> webException,
            Func<HttpResponseMessage, Task<T>> error)
        {
            if(TokenService.Instance.AccessTokenNeedsRefresh && !string.IsNullOrWhiteSpace(TokenService.Instance.RefreshToken))
            {
                var requestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(string.Concat(SettingsService.Instance.IdentityBaseUrl, "/connect/token")),
                    Content = new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            { "grant_type", "refresh_token" },
                            { "client_id", "connector" },
                            { "refresh_token", TokenService.Instance.RefreshToken }
                        })
                };

                try
                {
                    var response = await Client.SendAsync(requestMessage).ConfigureAwait(false);
                    if(!response.IsSuccessStatusCode)
                    {
                        if(response.StatusCode == HttpStatusCode.BadRequest)
                        {
                            response.StatusCode = HttpStatusCode.Unauthorized;
                        }

                        return await error.Invoke(response).ConfigureAwait(false);
                    }

                    var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);
                    TokenService.Instance.AccessToken = tokenResponse.AccessToken;
                    TokenService.Instance.RefreshToken = tokenResponse.RefreshToken;
                }
                catch
                {
                    return webException.Invoke();
                }
            }

            return success.Invoke();
        }

        protected async Task<ApiResult<T>> HandleErrorAsync<T>(HttpResponseMessage response)
        {
            try
            {
                var errors = await ParseErrorsAsync(response).ConfigureAwait(false);
                return ApiResult<T>.Failed(response.StatusCode, errors.ToArray());
            }
            catch
            { }

            return ApiResult<T>.Failed(response.StatusCode,
                new ApiError { Message = "An unknown error has occurred." });
        }

        protected async Task<ApiResult> HandleErrorAsync(HttpResponseMessage response)
        {
            try
            {
                var errors = await ParseErrorsAsync(response).ConfigureAwait(false);
                return ApiResult.Failed(response.StatusCode, errors.ToArray());
            }
            catch
            { }

            return ApiResult.Failed(response.StatusCode,
                new ApiError { Message = "An unknown error has occurred." });
        }

        private async Task<List<ApiError>> ParseErrorsAsync(HttpResponseMessage response)
        {
            var errors = new List<ApiError>();
            var statusCode = (int)response.StatusCode;
            if(statusCode >= 400 && statusCode <= 500)
            {
                ErrorResponse errorResponseModel = null;

                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if(!string.IsNullOrWhiteSpace(responseContent))
                {
                    var errorResponse = JObject.Parse(responseContent);
                    if(errorResponse["ErrorModel"] != null && errorResponse["ErrorModel"]["Message"] != null)
                    {
                        errorResponseModel = errorResponse["ErrorModel"].ToObject<ErrorResponse>();
                    }
                    else if(errorResponse["Message"] != null)
                    {
                        errorResponseModel = errorResponse.ToObject<ErrorResponse>();
                    }
                }

                if(errorResponseModel != null)
                {
                    if((errorResponseModel.ValidationErrors?.Count ?? 0) > 0)
                    {
                        foreach(var valError in errorResponseModel.ValidationErrors)
                        {
                            foreach(var errorMessage in valError.Value)
                            {
                                errors.Add(new ApiError { Message = errorMessage });
                            }
                        }
                    }
                    else
                    {
                        errors.Add(new ApiError { Message = errorResponseModel.Message });
                    }
                }
            }

            if(errors.Count == 0)
            {
                errors.Add(new ApiError { Message = "An unknown error has occurred." });
            }

            return errors;
        }
    }
}
