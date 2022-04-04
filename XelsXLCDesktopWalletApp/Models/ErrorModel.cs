using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models
{
  public  class ErrorModel
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string Description { get; set; }

        public ErrorModel[] Errors { get; set; }
        public List<ErrorModel> Errorss { get; set; }
    }

}
