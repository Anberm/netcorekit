using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using IdentityModel;
using IdentityModel.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NetCoreKit.Utils.Extensions;

namespace NetCoreKit.Infrastructure.GrpcHost
{
    public class AuthNInterceptor : Interceptor
    {
        private readonly IHostingEnvironment _env;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthNInterceptor> _logger;

        public AuthNInterceptor(IServiceProvider resolver)
        {
            _env = resolver.GetService<IHostingEnvironment>();
            _config = resolver.GetService<IConfiguration>();
            _logger = resolver.GetService<ILoggerFactory>()?.CreateLogger<AuthNInterceptor>();
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
            ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            try
            {
                var attribute = (CheckPolicyAttribute)continuation.Method
                    .GetCustomAttributes(typeof(CheckPolicyAttribute), false).FirstOrDefault();
                if (attribute == null)
                {
                    return await continuation(request, context);
                }

                if (context.RequestHeaders.All(x => x.Key != "authorization"))
                {
                    throw new UnauthorizedAccessException("Provide bearer token in the header.");
                }

                if (_config.GetSection("Idp") == null)
                {
                    throw new Exception("Provide Idp configuration section in appsettings.json.");
                }

                var client = new HttpClient();

                var idpConfig = _config.GetSection("Idp");
                var discoveryRequest = new DiscoveryDocumentRequest
                {
                    Address = idpConfig.GetValue<string>("Authority"),
                    Policy =
                    {
                        Authority = idpConfig.GetValue<string>("Authority"),
                        RequireHttps = false, // TODO: for demo only
                        ValidateIssuerName = false, // TODO: for demo only
                    }
                };

                var disco = await client.GetDiscoveryDocumentAsync(discoveryRequest);
                if (disco?.KeySet == null)
                {
                    throw new Exception(
                        $"Cannot discover IdpServer with Authority={idpConfig.GetValue<string>("Authority")} and Audience={idpConfig.GetValue<string>("Audience")}.");
                }

                var keys = new List<SecurityKey>();
                foreach (var webKey in disco.KeySet.Keys)
                {
                    var e = Base64Url.Decode(webKey.E);
                    var n = Base64Url.Decode(webKey.N);

                    var key = new RsaSecurityKey(new RSAParameters { Exponent = e, Modulus = n })
                    {
                        KeyId = webKey.Kid
                    };

                    keys.Add(key);
                }

                var parameters = new TokenValidationParameters
                {
                    ValidIssuer = disco.Issuer,
                    ValidAudience = idpConfig.GetValue<string>("Audience"),
                    IssuerSigningKeys = keys,

                    NameClaimType = JwtClaimTypes.Name,
                    RoleClaimType = JwtClaimTypes.Role,

                    RequireSignedTokens = true,
                    ValidateLifetime = true
                };

                var handler = new JwtSecurityTokenHandler();
                handler.InboundClaimTypeMap.Clear();

                var userToken = context.RequestHeaders.FirstOrDefault(x => x.Key == "authorization")?.Value;

                if (string.IsNullOrEmpty(userToken))
                {
                    throw new AuthenticationException("Cannot get authorization on the header.");
                }

                var user = handler.ValidateToken(userToken.TrimStart("Bearer").TrimStart("bearer").TrimStart(" "),
                    parameters, out _);

                if (user == null)
                {
                    throw new AuthenticationException(
                        "Cannot validate jwt token. Check authority and audience configuration.");
                }

                if (!user.HasClaim(c => c.Value == attribute.Name))
                {
                    throw new AuthenticationException("Cannot access to this API, please check your permission.");
                }

                return await continuation(request, context);
            }
            catch (Exception ex)
            {
                // http://avi.im/grpc-errors
                _logger.LogError(ex.Message);
                _logger.LogError(ex.StackTrace);
                throw new RpcException(new Status(StatusCode.Unauthenticated, ex.Message));
            }
        }
    }
}
