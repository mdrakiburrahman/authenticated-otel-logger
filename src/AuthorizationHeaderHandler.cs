namespace AuthenticatedOtelLogger
{
    public class AuthorizationHeaderHandler : DelegatingHandler
    {
        private readonly AuthorizationEnvironmentOptions _options;

        public AuthorizationHeaderHandler(
            HttpMessageHandler innerHandler,
            AuthorizationEnvironmentOptions options =
                AuthorizationEnvironmentOptions.LocalDevMachine
        )
            : base(innerHandler)
        {
            _options = options;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken
        )
        {
            request.Headers.Add("Authorization", GenerateAuthorizationHeaderValue());
            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.Headers.Add("Authorization", GenerateAuthorizationHeaderValue());
            return base.Send(request, cancellationToken);
        }

        private string GenerateAuthorizationHeaderValue()
        {
            Console.WriteLine("=====================================");
            Console.WriteLine("Generating authorization header value");
            Console.WriteLine("=====================================");
            return $"Bearer {DateTime.Now.Ticks}";
        }
    }
}
