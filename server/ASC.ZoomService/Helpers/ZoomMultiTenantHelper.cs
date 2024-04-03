using ASC.Security.Cryptography;

namespace ASC.ZoomService.Helpers
{
    [Scope]
    public class ZoomMultiTenantHelper
    {
        private readonly HostedSolution _hostedSolution;
        private readonly PasswordHasher _passwordHasher;

        public ZoomMultiTenantHelper(HostedSolution hostedSolution, PasswordHasher passwordHasher)
        {
            _hostedSolution = hostedSolution;
            _passwordHasher = passwordHasher;
        }

        public async Task<List<Tenant>> FindTenantsAsync(string login, string password = null, string passwordHash = null)
        {
            try
            {
                if (string.IsNullOrEmpty(passwordHash) && !string.IsNullOrEmpty(password))
                {
                    passwordHash = _passwordHasher.GetClientPassword(password);
                }

                return await _hostedSolution.FindTenantsAsync(login, passwordHash);
            }
            catch
            {
                throw;
            }
        }
    }
}
