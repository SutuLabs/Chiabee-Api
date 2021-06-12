﻿namespace WebApi.Services
{
    using System.Collections.Generic;
    using System.Linq;
    using WebApi.Services.ServerCommands;
    using static WebApi.Services.ServerCommands.CommandHelper;

    public class TemporarySerialNumbers
    {
        public static Dictionary<string, string> SerialNumbers;
        static TemporarySerialNumbers()
        {
            SerialNumbers = s.CleanSplit()
                .Select(_ => _.CleanSplit("\t"))
                .ToDictionary(_ => _[1], _ => _[0]);
        }

        private const string s = @"A001	Z1Z1QWBG
A002	Z1Z4A7RS
A003	Z1Z2WHTW
A004	Z1Z47BWQ
A005	Z1Z2W424
A006	Z1Z4AT2D
A007	Z1Z35KPC
A008	Z1Z1R2E7
A009	Z1Z7Y1ZB
A010	Z1Z60NGF
A011	Z1Z604S7
A012	Z1Z2Y0TR
A013	Z1Z60EYM
A014	Z1Z5Z9D5
A015	Z1Z60JEA
A016	Z1Z4DA5P
A017	Z1Z799TD
A018	Z1Z4CZTG
A019	Z1Z6DTD3
A020	Z1Z6MS1X
A021	Z1Z6C8S8
A022	Z1Z8VEPV
A023	Z1Z4B739
A024	Z1Z11VDW
A025	Z1Z5Q98W
A026	Z1Z60KGP
A027	Z1Z5ZA7A
A028	Z1Z4BEM3
A029	Z1Z60LZ4
A030	Z1Z5Z9LV
A031	Z1Z5ZAGD
A032	Z1Z5ZAFH
A033	Z1Z4ATYA
A034	Z1Z8VRTX
A035	Z1Z60J76
A036	Z1Z2WPKP
A037	Z1Z5NGXV
A038	Z1Z2X0YA
A039	Z1Z2YFG2
A040	Z1Z4GABS
A041	Z1Z4AA1Y
A042	Z1Z60MB8
A043	Z1Z5EV7F
A044	Z1Z13M47
A045	Z1Z1QQ54
A046	Z1Z60LCJ
A047	Z1Z60544
A048	Z1Z60KKK
A049	Z1Z4BSD0
A050	Z1Z1RKQF
A051	Z1Z4AFT3
A052	Z1Z4B25H
A053	Z1Z60685
A054	Z1Z30PBY
A055	Z1Z60L5F
A056	Z1Z6081W
A057	Z1Z8VN52
A058	Z1Z8WCJ2
A059	Z1Z2ZF9P
A060	Z1Z2X13C
A061	Z1Z34LMT
A062	Z1Z2YFHY
A063	Z1Z4GA4F
A064	Z1Z5ZAYX
A065	Z1Z60JH2
A066	Z1Z60L9N
A067	ZA4H832K
A068	ZA4H834D
A069	ZA4H833N
A070	ZA4H8340
A071	58E3KWDHFMYB
A072	58KZKDOSFMYB
A073	58COKCBLFMYB
A074	58EZKDCUFMYB
A075	58DDKEOQFMYB
A076	58CWKGJ0FMYB
A077	58CFKVMFFMYB
A078	28DAK2TDFMYB
A079	58IEK9P2FMYB
A080	58FSKKD3FMYB
A081	58DEK9FNFMYB
A082	28D7KAE3FMYB
A083	58KZKDSMFMYB
A084	58KDKFKZFMYB
A085	58F6KP6WFMYB
A086	ZC11JEE0
A087	ZC18KLZ7
A088	ZC13G52L
A089	ZC138DM0
A090	ZC13GM0K
A091	ZC10X4G2
A092	ZC18KM0S
A093	ZC13MCDP
A094	ZC13EK1X
A095	ZC13GTZK
A096	Z1Z15EAJ
A097	Z1Z8XZLQ
A098	58EYKFPHFMYB
A099	58C5KO5JFMYB
A100	Z1Z8VDPZ
A101	Z1Z4GRJR
A102	58IDKF0DFMYB
A103	58D5KO8NFMYB
A104	Z1Z6XRVC
A105	Z1Z8XNGH";
    }
}