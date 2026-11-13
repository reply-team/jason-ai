export const story = {
  "seller": {
    "name": "Tillerwave Dispatch Software",
    "domain": "tillerwave.example"
  },
  "operator": {
    "name": "Dana Whitfield",
    "actor": "dana.whitfield"
  },
  "project": {
    "name": "Tillerwave Fleet Outreach"
  },
  "senders": [
    {
      "external_id": "demo-sender-email",
      "channel": "email",
      "name": "Dana Whitfield",
      "address": "dana@tillerwave.example"
    },
    {
      "external_id": "demo-sender-linkedin",
      "channel": "linkedin",
      "name": "Dana Whitfield",
      "address": null
    }
  ],
  "campaigns": [
    {
      "key": "regional-fleets-q4",
      "name": "Regional Fleets Q4",
      "status": "active",
      "description": "Ops and dispatch leaders at regional trucking fleets of 40 to 220 trucks, before their Q1 planning."
    },
    {
      "key": "last-mile-pilot-invite",
      "name": "Last-Mile Pilot Invite",
      "status": "active",
      "description": "An invitation to a free 30-day route-planning pilot for last-mile delivery firms of 20 to 80 vehicles."
    },
    {
      "key": "dormant-accounts-revival",
      "name": "Dormant Accounts Revival",
      "status": "paused",
      "description": "Fleets that took a demo last year and went quiet; paused while the new pricing is settled."
    },
    {
      "key": "cold-chain-carriers",
      "name": "Cold Chain Carriers",
      "status": "draft",
      "description": "Refrigerated carriers, where a missed delivery window spoils the load. Not started yet."
    }
  ],
  "companies": [
    {
      "key": "tamarack-ridge",
      "name": "Tamarack Ridge Freight",
      "domain": "tamarackridgefreight.example",
      "city": "Duluth, MN",
      "region": "Upper Midwest",
      "trucks": 140
    },
    {
      "key": "coppervale",
      "name": "Coppervale Haulers",
      "domain": "coppervalehaulers.example",
      "city": "Tucson, AZ",
      "region": "Southwest",
      "trucks": 85
    },
    {
      "key": "larkfield",
      "name": "Larkfield Transport Co.",
      "domain": "larkfieldtransport.example",
      "city": "Boise, ID",
      "region": "Mountain West",
      "trucks": 60
    },
    {
      "key": "bramblewood",
      "name": "Bramblewood Carriers",
      "domain": "bramblewoodcarriers.example",
      "city": "Knoxville, TN",
      "region": "Southeast",
      "trucks": 210
    },
    {
      "key": "ninebark",
      "name": "Ninebark Logistics",
      "domain": "ninebarklogistics.example",
      "city": "Fargo, ND",
      "region": "Upper Midwest",
      "trucks": 45
    },
    {
      "key": "saltmarsh",
      "name": "Saltmarsh Trucking",
      "domain": "saltmarshtrucking.example",
      "city": "Savannah, GA",
      "region": "Southeast",
      "trucks": 120
    },
    {
      "key": "hollowpine",
      "name": "Hollowpine Freight Lines",
      "domain": "hollowpinefreight.example",
      "city": "Spokane, WA",
      "region": "Pacific Northwest",
      "trucks": 95
    },
    {
      "key": "quarrystone",
      "name": "Quarrystone Transport",
      "domain": "quarrystonetransport.example",
      "city": "Joplin, MO",
      "region": "Midwest",
      "trucks": 175
    },
    {
      "key": "sparrowgate",
      "name": "Sparrowgate Delivery",
      "domain": "sparrowgatedelivery.example",
      "city": "Columbus, OH",
      "region": "Midwest",
      "trucks": 70
    },
    {
      "key": "pebbleford",
      "name": "Pebbleford Couriers",
      "domain": "pebblefordcouriers.example",
      "city": "Sacramento, CA",
      "region": "West",
      "trucks": 38
    },
    {
      "key": "juniper-spoke",
      "name": "Juniper Spoke Logistics",
      "domain": "juniperspoke.example",
      "city": "Raleigh, NC",
      "region": "Southeast",
      "trucks": 55
    },
    {
      "key": "tidewheel",
      "name": "Tidewheel Last Mile",
      "domain": "tidewheel.example",
      "city": "Providence, RI",
      "region": "Northeast",
      "trucks": 28
    },
    {
      "key": "ironbarrow",
      "name": "Ironbarrow Haulage",
      "domain": "ironbarrowhaulage.example",
      "city": "Pittsburgh, PA",
      "region": "Northeast",
      "trucks": 160
    },
    {
      "key": "wrenfield",
      "name": "Wrenfield Freight",
      "domain": "wrenfieldfreight.example",
      "city": "Omaha, NE",
      "region": "Midwest",
      "trucks": 50
    },
    {
      "key": "cobaltcreek",
      "name": "Cobaltcreek Transport",
      "domain": "cobaltcreektransport.example",
      "city": "Tulsa, OK",
      "region": "South Central",
      "trucks": 90
    },
    {
      "key": "mossgrove",
      "name": "Mossgrove Carriers",
      "domain": "mossgrovecarriers.example",
      "city": "Burlington, VT",
      "region": "Northeast",
      "trucks": 24
    },
    {
      "key": "glacierwick",
      "name": "Glacierwick Cold Haul",
      "domain": "glacierwickcoldhaul.example",
      "city": "Madison, WI",
      "region": "Upper Midwest",
      "trucks": 110
    },
    {
      "key": "chillbrook",
      "name": "Chillbrook Reefer Lines",
      "domain": "chillbrookreefer.example",
      "city": "Yakima, WA",
      "region": "Pacific Northwest",
      "trucks": 65
    }
  ],
  "people": [
    {
      "key": "p01",
      "contact_id": "demo-contact-p01",
      "first_name": "Gwen",
      "last_name": "Halvorsen",
      "title": "Director of Operations",
      "company": "tamarack-ridge",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "gwen.halvorsen@tamarackridgefreight.example"
        },
        {
          "channel": "phone",
          "value": "+12185550100"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s01",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 140 trucks without the whiteboard",
          "body": "Hi Gwen,\n\nI have been reading about Tamarack Ridge Freight: a fleet of about 140 trucks that runs grain and building-supply loads across three states. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s01",
          "offset_seconds": 40,
          "body": "Hi Dana,\n\nTiming is good. We are re-bidding our planning tool in January and I would rather see options now. Thursday after 2 pm Central works for me.\n\nGwen",
          "meeting_intent": true
        }
      ]
    },
    {
      "key": "p02",
      "contact_id": "demo-contact-p02",
      "first_name": "Marcus",
      "last_name": "Teague",
      "title": "Dispatch Manager",
      "company": "tamarack-ridge",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "marcus.teague@tamarackridgefreight.example"
        },
        {
          "channel": "phone",
          "value": "+12185550101"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s02",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Tamarack Ridge Freight",
          "body": "Hi Marcus,\n\nI have been reading about Tamarack Ridge Freight: a fleet of about 140 trucks that runs grain and building-supply loads across three states. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p03",
      "contact_id": "demo-contact-p03",
      "first_name": "Ruth",
      "last_name": "Okafor",
      "title": "Owner",
      "company": "tamarack-ridge",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "ruth.okafor@tamarackridgefreight.example"
        },
        {
          "channel": "phone",
          "value": "+12185550102"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s03",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Upper Midwest",
          "body": "Hi Ruth,\n\nI have been reading about Tamarack Ridge Freight: a fleet of about 140 trucks that runs grain and building-supply loads across three states. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p04",
      "contact_id": "demo-contact-p04",
      "first_name": "Hector",
      "last_name": "Villanueva",
      "title": "VP of Operations",
      "company": "coppervale",
      "time_zone": "America/Phoenix",
      "channels": [
        {
          "channel": "email",
          "value": "hector.villanueva@coppervalehaulers.example"
        },
        {
          "channel": "phone",
          "value": "+15205550103"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s04",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Coppervale Haulers and next-day route planning",
          "body": "Hi Hector,\n\nI have been reading about Coppervale Haulers: a fleet of about 85 trucks that plans around summer heat limits on afternoon runs. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p05",
      "contact_id": "demo-contact-p05",
      "first_name": "Leah",
      "last_name": "Brandt",
      "title": "Lead Dispatcher",
      "company": "coppervale",
      "time_zone": "America/Phoenix",
      "channels": [
        {
          "channel": "email",
          "value": "leah.brandt@coppervalehaulers.example"
        },
        {
          "channel": "phone",
          "value": "+15205550104"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s05",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 85 trucks without the whiteboard",
          "body": "Hi Leah,\n\nI have been reading about Coppervale Haulers: a fleet of about 85 trucks that plans around summer heat limits on afternoon runs. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p06",
      "contact_id": "demo-contact-p06",
      "first_name": "Owen",
      "last_name": "Pratchett",
      "title": "Fleet Manager",
      "company": "coppervale",
      "time_zone": "America/Phoenix",
      "channels": [
        {
          "channel": "email",
          "value": "owen.pratchett@coppervalehaulers.example"
        },
        {
          "channel": "phone",
          "value": "+15205550105"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s06",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Coppervale Haulers",
          "body": "Hi Owen,\n\nI have been reading about Coppervale Haulers: a fleet of about 85 trucks that plans around summer heat limits on afternoon runs. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p07",
      "contact_id": "demo-contact-p07",
      "first_name": "Tessa",
      "last_name": "Lindqvist",
      "title": "Operations Manager",
      "company": "larkfield",
      "time_zone": "America/Boise",
      "channels": [
        {
          "channel": "email",
          "value": "tessa.lindqvist@larkfieldtransport.example"
        },
        {
          "channel": "phone",
          "value": "+12085550106"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s07",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Mountain West",
          "body": "Hi Tessa,\n\nI have been reading about Larkfield Transport Co.: a fleet of about 60 trucks that added a second terminal this year. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Mountain West cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p08",
      "contact_id": "demo-contact-p08",
      "first_name": "Calvin",
      "last_name": "Moreau",
      "title": "President",
      "company": "larkfield",
      "time_zone": "America/Boise",
      "channels": [
        {
          "channel": "email",
          "value": "calvin.moreau@larkfieldtransport.example"
        },
        {
          "channel": "phone",
          "value": "+12085550107"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s08",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Larkfield Transport Co. and next-day route planning",
          "body": "Hi Calvin,\n\nI have been reading about Larkfield Transport Co.: a fleet of about 60 trucks that added a second terminal this year. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Mountain West cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s08",
          "offset_seconds": 75,
          "body": "Dana, thanks, but routes are not mine. Priya Raman runs dispatch here and she is the one to talk to. I have copied her.\n\nCalvin",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p09",
      "contact_id": "demo-contact-p09",
      "first_name": "Priya",
      "last_name": "Raman",
      "title": "Dispatch Supervisor",
      "company": "larkfield",
      "time_zone": "America/Boise",
      "channels": [
        {
          "channel": "email",
          "value": "priya.raman@larkfieldtransport.example"
        },
        {
          "channel": "phone",
          "value": "+12085550108"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s09",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 60 trucks without the whiteboard",
          "body": "Hi Priya,\n\nI have been reading about Larkfield Transport Co.: a fleet of about 60 trucks that added a second terminal this year. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Mountain West cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p10",
      "contact_id": "demo-contact-p10",
      "first_name": "Darnell",
      "last_name": "Fisk",
      "title": "Director of Dispatch",
      "company": "bramblewood",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "darnell.fisk@bramblewoodcarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18655550109"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s10",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Bramblewood Carriers",
          "body": "Hi Darnell,\n\nI have been reading about Bramblewood Carriers: a fleet of about 210 trucks that dispatches 210 trucks out of two yards. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s10",
          "offset_seconds": 130,
          "body": "What does this cost for a fleet our size? We looked at two other tools and both priced per dispatcher seat, which did not work for us.\n\nDarnell",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p11",
      "contact_id": "demo-contact-p11",
      "first_name": "Amelia",
      "last_name": "Corwin",
      "title": "COO",
      "company": "bramblewood",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "amelia.corwin@bramblewoodcarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18655550110"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s11",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Southeast",
          "body": "Hi Amelia,\n\nI have been reading about Bramblewood Carriers: a fleet of about 210 trucks that dispatches 210 trucks out of two yards. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p12",
      "contact_id": "demo-contact-p12",
      "first_name": "Jonah",
      "last_name": "Weller",
      "title": "Transportation Manager",
      "company": "bramblewood",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "jonah.weller@bramblewoodcarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18655550111"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s12",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Bramblewood Carriers and next-day route planning",
          "body": "Hi Jonah,\n\nI have been reading about Bramblewood Carriers: a fleet of about 210 trucks that dispatches 210 trucks out of two yards. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p13",
      "contact_id": "demo-contact-p13",
      "first_name": "Ingrid",
      "last_name": "Solberg",
      "title": "Owner",
      "company": "ninebark",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "ingrid.solberg@ninebarklogistics.example"
        },
        {
          "channel": "phone",
          "value": "+17015550112"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s13",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 45 trucks without the whiteboard",
          "body": "Hi Ingrid,\n\nI have been reading about Ninebark Logistics: a fleet of about 45 trucks that still plans next-day routes on a whiteboard and a spreadsheet. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s13",
          "offset_seconds": 190,
          "body": "I am out of the office until Monday with limited access to email. For urgent dispatch matters please call the main line.\n\nIngrid Solberg",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p14",
      "contact_id": "demo-contact-p14",
      "first_name": "Travis",
      "last_name": "Kemper",
      "title": "Dispatch Manager",
      "company": "ninebark",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "travis.kemper@ninebarklogistics.example"
        },
        {
          "channel": "phone",
          "value": "+17015550113"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s14",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Ninebark Logistics",
          "body": "Hi Travis,\n\nI have been reading about Ninebark Logistics: a fleet of about 45 trucks that still plans next-day routes on a whiteboard and a spreadsheet. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p15",
      "contact_id": "demo-contact-p15",
      "first_name": "Nadia",
      "last_name": "Petrov",
      "title": "Operations Coordinator",
      "company": "ninebark",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "nadia.petrov@ninebarklogistics.example"
        },
        {
          "channel": "phone",
          "value": "+17015550114"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s15",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Upper Midwest",
          "body": "Hi Nadia,\n\nI have been reading about Ninebark Logistics: a fleet of about 45 trucks that still plans next-day routes on a whiteboard and a spreadsheet. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Upper Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p16",
      "contact_id": "demo-contact-p16",
      "first_name": "Curtis",
      "last_name": "Langford",
      "title": "VP of Operations",
      "company": "saltmarsh",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "curtis.langford@saltmarshtrucking.example"
        },
        {
          "channel": "phone",
          "value": "+19125550115"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s16",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Saltmarsh Trucking and next-day route planning",
          "body": "Hi Curtis,\n\nI have been reading about Saltmarsh Trucking: a fleet of about 120 trucks that moves port containers on tight appointment windows. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s16",
          "offset_seconds": 260,
          "body": "Appreciate it, but we built our own planner in-house two years ago and we are happy with it. Not looking right now.\n\nCurtis",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p17",
      "contact_id": "demo-contact-p17",
      "first_name": "Simone",
      "last_name": "Achebe",
      "title": "Dispatch Manager",
      "company": "saltmarsh",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "simone.achebe@saltmarshtrucking.example"
        },
        {
          "channel": "phone",
          "value": "+19125550116"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s17",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 120 trucks without the whiteboard",
          "body": "Hi Simone,\n\nI have been reading about Saltmarsh Trucking: a fleet of about 120 trucks that moves port containers on tight appointment windows. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p18",
      "contact_id": "demo-contact-p18",
      "first_name": "Wade",
      "last_name": "Holloway",
      "title": "Terminal Manager",
      "company": "saltmarsh",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "wade.holloway@saltmarshtrucking.example"
        },
        {
          "channel": "phone",
          "value": "+19125550117"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s18",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Saltmarsh Trucking",
          "body": "Hi Wade,\n\nI have been reading about Saltmarsh Trucking: a fleet of about 120 trucks that moves port containers on tight appointment windows. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Southeast cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p19",
      "contact_id": "demo-contact-p19",
      "first_name": "Megan",
      "last_name": "Ashdown",
      "title": "Director of Operations",
      "company": "hollowpine",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "megan.ashdown@hollowpinefreight.example"
        },
        {
          "channel": "phone",
          "value": "+15095550118"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s19",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Pacific Northwest",
          "body": "Hi Megan,\n\nI have been reading about Hollowpine Freight Lines: a fleet of about 95 trucks that loses hours to mountain-pass closures every winter. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Pacific Northwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p20",
      "contact_id": "demo-contact-p20",
      "first_name": "Felix",
      "last_name": "Garrow",
      "title": "Lead Dispatcher",
      "company": "hollowpine",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "felix.garrow@hollowpinefreight.example"
        },
        {
          "channel": "phone",
          "value": "+15095550119"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s20",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Hollowpine Freight Lines and next-day route planning",
          "body": "Hi Felix,\n\nI have been reading about Hollowpine Freight Lines: a fleet of about 95 trucks that loses hours to mountain-pass closures every winter. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Pacific Northwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s20",
          "offset_seconds": 340,
          "body": "Does it read hours of service straight from the ELD, or does someone have to key them in? That is where every tool we tried fell over.\n\nFelix",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p21",
      "contact_id": "demo-contact-p21",
      "first_name": "Lorna",
      "last_name": "McBride",
      "title": "General Manager",
      "company": "hollowpine",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "lorna.mcbride@hollowpinefreight.example"
        },
        {
          "channel": "phone",
          "value": "+15095550120"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s21",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Planning 95 trucks without the whiteboard",
          "body": "Hi Lorna,\n\nI have been reading about Hollowpine Freight Lines: a fleet of about 95 trucks that loses hours to mountain-pass closures every winter. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Pacific Northwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p22",
      "contact_id": "demo-contact-p22",
      "first_name": "Russell",
      "last_name": "Danvers",
      "title": "President",
      "company": "quarrystone",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "russell.danvers@quarrystonetransport.example"
        },
        {
          "channel": "phone",
          "value": "+14175550121"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s22",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "A question about dispatch at Quarrystone Transport",
          "body": "Hi Russell,\n\nI have been reading about Quarrystone Transport: a fleet of about 175 trucks that runs a mixed fleet of flatbeds and dry vans. You probably see the cost of a late plan more than anyone: overtime, empty miles and the calls that follow.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s22",
          "offset_seconds": 450,
          "body": "Please take me off your list.\n\nRussell",
          "meeting_intent": false
        }
      ]
    },
    {
      "key": "p23",
      "contact_id": "demo-contact-p23",
      "first_name": "Keisha",
      "last_name": "Marlowe",
      "title": "Operations Manager",
      "company": "quarrystone",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "keisha.marlowe@quarrystonetransport.example"
        },
        {
          "channel": "phone",
          "value": "+14175550122"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s23",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Fewer empty miles across the Midwest",
          "body": "Hi Keisha,\n\nI have been reading about Quarrystone Transport: a fleet of about 175 trucks that runs a mixed fleet of flatbeds and dry vans. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s23",
          "offset_seconds": 600,
          "body": "Send me a one-pager and some numbers from a fleet like ours and I will put it in front of Russell. Next week is better for a call.\n\nKeisha",
          "meeting_intent": true
        }
      ]
    },
    {
      "key": "p24",
      "contact_id": "demo-contact-p24",
      "first_name": "Bennett",
      "last_name": "Szabo",
      "title": "Dispatch Manager",
      "company": "quarrystone",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "bennett.szabo@quarrystonetransport.example"
        },
        {
          "channel": "phone",
          "value": "+14175550123"
        }
      ],
      "campaign": "regional-fleets-q4",
      "sends": [
        {
          "key": "s24",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "Quarrystone Transport and next-day route planning",
          "body": "Hi Bennett,\n\nI have been reading about Quarrystone Transport: a fleet of about 175 trucks that runs a mixed fleet of flatbeds and dry vans. I suspect a good part of your morning goes into the plan before the first truck rolls.\n\nTillerwave builds the next day's routes for a regional fleet in a few minutes, with your drivers' hours, your customers' windows and the loads already booked, and lets a dispatcher move a stop by hand when the road says otherwise. Fleets of your size in the Midwest cut empty miles by around a tenth in their first quarter.\n\nWould a 20-minute look at your own lanes be worth it before you plan Q1?\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p25",
      "contact_id": "demo-contact-p25",
      "first_name": "Elena",
      "last_name": "Roswell",
      "title": "Operations Director",
      "company": "sparrowgate",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "elena.roswell@sparrowgatedelivery.example"
        },
        {
          "channel": "phone",
          "value": "+16145550124"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s25",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Sparrowgate Delivery",
          "body": "Hi Elena,\n\nSparrowgate Delivery delivers appliances with two-person crews, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Sparrowgate Delivery? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p26",
      "contact_id": "demo-contact-p26",
      "first_name": "Kofi",
      "last_name": "Mensah",
      "title": "Routing Manager",
      "company": "sparrowgate",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "kofi.mensah@sparrowgatedelivery.example"
        },
        {
          "channel": "phone",
          "value": "+16145550125"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s26",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Sparrowgate Delivery",
          "body": "Hi Kofi,\n\nSparrowgate Delivery delivers appliances with two-person crews, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Sparrowgate Delivery? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p27",
      "contact_id": "demo-contact-p27",
      "first_name": "Grant",
      "last_name": "Ellery",
      "title": "Owner",
      "company": "sparrowgate",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "grant.ellery@sparrowgatedelivery.example"
        },
        {
          "channel": "phone",
          "value": "+16145550126"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s27",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Sparrowgate Delivery",
          "body": "Hi Grant,\n\nSparrowgate Delivery delivers appliances with two-person crews, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Sparrowgate Delivery? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p28",
      "contact_id": "demo-contact-p28",
      "first_name": "Jasmine",
      "last_name": "Tolliver",
      "title": "General Manager",
      "company": "pebbleford",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "jasmine.tolliver@pebblefordcouriers.example"
        },
        {
          "channel": "phone",
          "value": "+19165550127"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s28",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Pebbleford Couriers",
          "body": "Hi Jasmine,\n\nPebbleford Couriers runs same-day pharmacy deliveries, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Pebbleford Couriers? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p29",
      "contact_id": "demo-contact-p29",
      "first_name": "Arturo",
      "last_name": "Bellamy",
      "title": "Dispatch Lead",
      "company": "pebbleford",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "arturo.bellamy@pebblefordcouriers.example"
        },
        {
          "channel": "phone",
          "value": "+19165550128"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s29",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Pebbleford Couriers",
          "body": "Hi Arturo,\n\nPebbleford Couriers runs same-day pharmacy deliveries, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Pebbleford Couriers? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p30",
      "contact_id": "demo-contact-p30",
      "first_name": "Hannah",
      "last_name": "Voss",
      "title": "Operations Manager",
      "company": "pebbleford",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "hannah.voss@pebblefordcouriers.example"
        },
        {
          "channel": "phone",
          "value": "+19165550129"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s30",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Pebbleford Couriers",
          "body": "Hi Hannah,\n\nPebbleford Couriers runs same-day pharmacy deliveries, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Pebbleford Couriers? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p31",
      "contact_id": "demo-contact-p31",
      "first_name": "Mitchell",
      "last_name": "Crane",
      "title": "Founder",
      "company": "juniper-spoke",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "mitchell.crane@juniperspoke.example"
        },
        {
          "channel": "phone",
          "value": "+19195550130"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s31",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Juniper Spoke Logistics",
          "body": "Hi Mitchell,\n\nJuniper Spoke Logistics grew from 30 to 55 vans in a year, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Juniper Spoke Logistics? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p32",
      "contact_id": "demo-contact-p32",
      "first_name": "Lucia",
      "last_name": "Ferrante",
      "title": "Head of Operations",
      "company": "juniper-spoke",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "lucia.ferrante@juniperspoke.example"
        },
        {
          "channel": "phone",
          "value": "+19195550131"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s32",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Juniper Spoke Logistics",
          "body": "Hi Lucia,\n\nJuniper Spoke Logistics grew from 30 to 55 vans in a year, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Juniper Spoke Logistics? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p33",
      "contact_id": "demo-contact-p33",
      "first_name": "Devon",
      "last_name": "Ashby",
      "title": "Fleet Supervisor",
      "company": "juniper-spoke",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "devon.ashby@juniperspoke.example"
        },
        {
          "channel": "phone",
          "value": "+19195550132"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s33",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Juniper Spoke Logistics",
          "body": "Hi Devon,\n\nJuniper Spoke Logistics grew from 30 to 55 vans in a year, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Juniper Spoke Logistics? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p34",
      "contact_id": "demo-contact-p34",
      "first_name": "Rosalind",
      "last_name": "Pike",
      "title": "Owner",
      "company": "tidewheel",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "rosalind.pike@tidewheel.example"
        },
        {
          "channel": "phone",
          "value": "+14015550133"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [
        {
          "key": "s34",
          "operation": "message.send",
          "channel": "email",
          "approval": "waiting",
          "subject": "A free route-planning pilot for Tidewheel Last Mile",
          "body": "Hi Rosalind,\n\nTidewheel Last Mile delivers furniture into narrow city streets, and last-mile work like that lives or dies on the route sheet. We are opening a free 30-day pilot of Tillerwave's route planner for ten delivery firms of 20 to 80 vehicles this winter.\n\nThe pilot plans your real stops every evening, you keep your own dispatch as the check, and at the end you see the miles and the late arrivals side by side. No contract, and nothing to install on the vans.\n\nWould you like one of the ten places for Tidewheel Last Mile? A reply with a good day for a 15-minute call is all it takes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p35",
      "contact_id": "demo-contact-p35",
      "first_name": "Samir",
      "last_name": "Haddad",
      "title": "Dispatch Manager",
      "company": "tidewheel",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "samir.haddad@tidewheel.example"
        },
        {
          "channel": "phone",
          "value": "+14015550134"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [],
      "replies": []
    },
    {
      "key": "p36",
      "contact_id": "demo-contact-p36",
      "first_name": "Colleen",
      "last_name": "Brady",
      "title": "Customer Operations Lead",
      "company": "tidewheel",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "colleen.brady@tidewheel.example"
        },
        {
          "channel": "phone",
          "value": "+14015550135"
        }
      ],
      "campaign": "last-mile-pilot-invite",
      "sends": [],
      "replies": []
    },
    {
      "key": "p37",
      "contact_id": "demo-contact-p37",
      "first_name": "Victor",
      "last_name": "Lindgren",
      "title": "VP of Operations",
      "company": "ironbarrow",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "victor.lindgren@ironbarrowhaulage.example"
        },
        {
          "channel": "phone",
          "value": "+14125550136"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [
        {
          "key": "s35",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "What changed at Tillerwave since we spoke",
          "body": "Hi Victor,\n\nWhen we last spoke, Ironbarrow Haulage took a demo last spring, and the timing was not right. Two things have changed since: the planner now handles mixed fleets and split shifts, and pricing starts per truck rather than per seat, which suits a fleet of 160.\n\nIf planning is on your list for the new year, I would be glad to show you what is new in 20 minutes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p38",
      "contact_id": "demo-contact-p38",
      "first_name": "Dolores",
      "last_name": "Quintero",
      "title": "Dispatch Manager",
      "company": "ironbarrow",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "dolores.quintero@ironbarrowhaulage.example"
        },
        {
          "channel": "phone",
          "value": "+14125550137"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p39",
      "contact_id": "demo-contact-p39",
      "first_name": "Abe",
      "last_name": "Whitcombe",
      "title": "Owner",
      "company": "ironbarrow",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "abe.whitcombe@ironbarrowhaulage.example"
        },
        {
          "channel": "phone",
          "value": "+14125550138"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [
        {
          "key": "s36",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "What changed at Tillerwave since we spoke",
          "body": "Hi Abe,\n\nWhen we last spoke, Ironbarrow Haulage took a demo last spring, and the timing was not right. Two things have changed since: the planner now handles mixed fleets and split shifts, and pricing starts per truck rather than per seat, which suits a fleet of 160.\n\nIf planning is on your list for the new year, I would be glad to show you what is new in 20 minutes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p40",
      "contact_id": "demo-contact-p40",
      "first_name": "Shauna",
      "last_name": "Kessler",
      "title": "Operations Manager",
      "company": "wrenfield",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "shauna.kessler@wrenfieldfreight.example"
        },
        {
          "channel": "phone",
          "value": "+14025550139"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [
        {
          "key": "s37",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "What changed at Tillerwave since we spoke",
          "body": "Hi Shauna,\n\nWhen we last spoke, Wrenfield Freight asked for pricing last year and went quiet, and the timing was not right. Two things have changed since: the planner now handles mixed fleets and split shifts, and pricing starts per truck rather than per seat, which suits a fleet of 50.\n\nIf planning is on your list for the new year, I would be glad to show you what is new in 20 minutes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": [
        {
          "send": "s37",
          "offset_seconds": 720,
          "body": "Hi Dana, good to hear from you. Still interested, and the budget opens in Q1. Let us talk in the first week of January.\n\nShauna",
          "meeting_intent": true
        }
      ]
    },
    {
      "key": "p41",
      "contact_id": "demo-contact-p41",
      "first_name": "Lyle",
      "last_name": "Ambrose",
      "title": "President",
      "company": "wrenfield",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "lyle.ambrose@wrenfieldfreight.example"
        },
        {
          "channel": "phone",
          "value": "+14025550140"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [
        {
          "key": "s38",
          "operation": "message.send",
          "channel": "email",
          "approval": "given",
          "subject": "What changed at Tillerwave since we spoke",
          "body": "Hi Lyle,\n\nWhen we last spoke, Wrenfield Freight asked for pricing last year and went quiet, and the timing was not right. Two things have changed since: the planner now handles mixed fleets and split shifts, and pricing starts per truck rather than per seat, which suits a fleet of 50.\n\nIf planning is on your list for the new year, I would be glad to show you what is new in 20 minutes.\n\nDana Whitfield\nTillerwave Dispatch Software",
          "note": null
        }
      ],
      "replies": []
    },
    {
      "key": "p42",
      "contact_id": "demo-contact-p42",
      "first_name": "Mei",
      "last_name": "Tanaka",
      "title": "Lead Dispatcher",
      "company": "wrenfield",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "mei.tanaka@wrenfieldfreight.example"
        },
        {
          "channel": "phone",
          "value": "+14025550141"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p43",
      "contact_id": "demo-contact-p43",
      "first_name": "Boyd",
      "last_name": "Carraway",
      "title": "Director of Operations",
      "company": "cobaltcreek",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "boyd.carraway@cobaltcreektransport.example"
        },
        {
          "channel": "phone",
          "value": "+19185550142"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p44",
      "contact_id": "demo-contact-p44",
      "first_name": "Renata",
      "last_name": "Silva",
      "title": "Fleet Manager",
      "company": "cobaltcreek",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "renata.silva@cobaltcreektransport.example"
        },
        {
          "channel": "phone",
          "value": "+19185550143"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p45",
      "contact_id": "demo-contact-p45",
      "first_name": "Garrett",
      "last_name": "Nunley",
      "title": "Dispatch Manager",
      "company": "cobaltcreek",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "garrett.nunley@cobaltcreektransport.example"
        },
        {
          "channel": "phone",
          "value": "+19185550144"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p46",
      "contact_id": "demo-contact-p46",
      "first_name": "Ellen",
      "last_name": "Prescott",
      "title": "Owner",
      "company": "mossgrove",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "ellen.prescott@mossgrovecarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18025550145"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p47",
      "contact_id": "demo-contact-p47",
      "first_name": "Tobias",
      "last_name": "Wynn",
      "title": "Operations Lead",
      "company": "mossgrove",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "tobias.wynn@mossgrovecarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18025550146"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p48",
      "contact_id": "demo-contact-p48",
      "first_name": "Marisol",
      "last_name": "Ortega",
      "title": "Dispatcher",
      "company": "mossgrove",
      "time_zone": "America/New_York",
      "channels": [
        {
          "channel": "email",
          "value": "marisol.ortega@mossgrovecarriers.example"
        },
        {
          "channel": "phone",
          "value": "+18025550147"
        }
      ],
      "campaign": "dormant-accounts-revival",
      "sends": [],
      "replies": []
    },
    {
      "key": "p49",
      "contact_id": "demo-contact-p49",
      "first_name": "Harlan",
      "last_name": "Vogt",
      "title": "VP of Operations",
      "company": "glacierwick",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "harlan.vogt@glacierwickcoldhaul.example"
        },
        {
          "channel": "phone",
          "value": "+16085550148"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    },
    {
      "key": "p50",
      "contact_id": "demo-contact-p50",
      "first_name": "Bridget",
      "last_name": "Nolan",
      "title": "Dispatch Manager",
      "company": "glacierwick",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "bridget.nolan@glacierwickcoldhaul.example"
        },
        {
          "channel": "phone",
          "value": "+16085550149"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    },
    {
      "key": "p51",
      "contact_id": "demo-contact-p51",
      "first_name": "Isaac",
      "last_name": "Merriweather",
      "title": "Compliance Manager",
      "company": "glacierwick",
      "time_zone": "America/Chicago",
      "channels": [
        {
          "channel": "email",
          "value": "isaac.merriweather@glacierwickcoldhaul.example"
        },
        {
          "channel": "phone",
          "value": "+16085550150"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    },
    {
      "key": "p52",
      "contact_id": "demo-contact-p52",
      "first_name": "Yolanda",
      "last_name": "Reyes",
      "title": "Operations Director",
      "company": "chillbrook",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "yolanda.reyes@chillbrookreefer.example"
        },
        {
          "channel": "phone",
          "value": "+15095550151"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    },
    {
      "key": "p53",
      "contact_id": "demo-contact-p53",
      "first_name": "Patrick",
      "last_name": "Ellison",
      "title": "Owner",
      "company": "chillbrook",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "patrick.ellison@chillbrookreefer.example"
        },
        {
          "channel": "phone",
          "value": "+15095550152"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    },
    {
      "key": "p54",
      "contact_id": "demo-contact-p54",
      "first_name": "Anika",
      "last_name": "Brooks",
      "title": "Dispatch Supervisor",
      "company": "chillbrook",
      "time_zone": "America/Los_Angeles",
      "channels": [
        {
          "channel": "email",
          "value": "anika.brooks@chillbrookreefer.example"
        },
        {
          "channel": "phone",
          "value": "+15095550153"
        }
      ],
      "campaign": "cold-chain-carriers",
      "sends": [],
      "replies": []
    }
  ]
};
