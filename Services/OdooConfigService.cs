using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PFE.Services
{
    public class OdooConfigService
    {
        public string OdooUrl { get; set; } = "https://ipl-pfe-2025-groupe11.odoo.com";
        public string OdooDb { get; set; } = "ipl-pfe-2025-groupe11-main-26040231";
        public int UserId { get; set; }
        public string UserPassword { get; set; } = string.Empty;

    }
}
