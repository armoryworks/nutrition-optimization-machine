\if :{?names}
\else
\set names ''
\endif
\if :{?sample}
\else
\set sample 20
\endif
\if :{?seed}
\else
\set seed 'diet-gate-count'
\endif
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
BEGIN READ ONLY;
WITH consts AS (
    SELECT '\m(?:wines?|vins?|beers?|ales?|lager|stout|porter|pilsn?er|ipa|bock|guinness|ciders?|perry|scrumpy|vodka|rum|whiske?y|bourbon|scotch|brandy|brandies|cognac|armagnac|calvados|applejack|kirsch|kirschwasser|grappa|gin|hollands|tequila|mezcal|arrack|arak|pisco|everclear|eau|schnapps?|liqueurs?|liquor|sake|mirin|shaoxing|shaohsing|hsing|sherry|sack|port|madeira|marsala|tokay|tokaji|malaga|muscatel|moscatel|amontillado|fino|oloroso|manzanilla|sercial|malmsey|vermouth|champagne|prosecco|cava|sekt|asti|claret|burgundy|bordeaux|chablis|hocks?|rhenish|moselle|riesling|chardonnay|sauvignon|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot|sangiovese|gamay|grenache|tempranillo|barolo|valpolicella|beaujolais|sauternes?|muscadet|sancerre|soave|gew[uü]rztraminer|s[eé]millon|frascati|orvieto|catawba|ros[eé]|kahlua|baileys|bailey|cura[cç]ao|cura[cç]oa|cointreau|marnier|amaretto|frangelico|chartreuse|b[eé]n[eé]dictine|drambuie|limoncello|sambuca|anisette|ratafia|cassis|cr[eè]me|menthe|ouzo|pastis|absinthe|galliano|strega|advocaat|noyau|noyeau|maraschino|bitters|angostura|spirits?|alcohol|mead|eiswein|icewine)\M'::text AS drink,
           '\m(?:vinegars?|vinaigrettes?|extracts?|essences?|flavou?r\w*|yeasts?|juices?|root beer|ginger ale|ginger beer|birch beer|non-?alcoholic|alcohol-free|dealcoholi[sz]ed|cr[eè]me fra[iî]che|port salut)\M'::text AS not_drink
),
rule_pats (pattern, seed, term_rx, blank_rx) AS (
    VALUES
        ('% oat%', '%oat%', '(?:[^[:alpha:]]oat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$)))', NULL),
        ('%aioli%', '%aioli%', '(?:(?:^|[^[:alpha:]])aioli(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}aioli(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%alfredo%', '%alfredo%', '(?:(?:^|[^[:alpha:]])alfredo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}alfredo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%almond butter%', '%almond%', '(?:(?:^|[^[:alpha:]])almond[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}almond[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%almond%', '%almond%', '(?:(?:^|[^[:alpha:]])almond(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}almond(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%amaretti%', '%amaretti%', '(?:(?:^|[^[:alpha:]])amaretti(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}amaretti(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%anchov%', '%anchov%', '(?:(?:^|[^[:alpha:]])anchov(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}anchov(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%apple%', '%apple%', '(?:(?:^|[^[:alpha:]])apple(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}apple(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:pineapple)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%asiago%', '%asiago%', '(?:(?:^|[^[:alpha:]])asiago(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}asiago(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%aubergine%', '%aubergine%', '(?:(?:^|[^[:alpha:]])aubergine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}aubergine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%avocado%', '%avocado%', '(?:(?:^|[^[:alpha:]])avocado(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}avocado(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bacon%', '%bacon%', '(?:(?:^|[^[:alpha:]])bacon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bacon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:turkey(?:[''’]?s[''’]?)?[[:space:]-]+bacon|beef(?:[''’]?s[''’]?)?[[:space:]-]+bacon)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%bagel%', '%bagel%', '(?:(?:^|[^[:alpha:]])bagel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bagel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%baklava%', '%baklava%', '(?:(?:^|[^[:alpha:]])baklava(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}baklava(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%banana%', '%banana%', '(?:(?:^|[^[:alpha:]])banana(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}banana(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:banana(?:[''’]?s[''’]?)?[[:space:]-]+pepper)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%barley%', '%barley%', '(?:(?:^|[^[:alpha:]])barley(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}barley(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bass%', '%bass%', '(?:(?:^|[^[:alpha:]])bass(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bass(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bean%', '%bean%', '(?:(?:^|[^[:alpha:]])bean(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bean(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:vanilla(?:[''’]?s[''’]?)?[[:space:]-]+bean|coffee(?:[''’]?s[''’]?)?[[:space:]-]+bean|cocoa(?:[''’]?s[''’]?)?[[:space:]-]+bean|cacao(?:[''’]?s[''’]?)?[[:space:]-]+bean)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%bearnaise%', '%bearnaise%', '(?:(?:^|[^[:alpha:]])bearnaise(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bearnaise(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bechamel%', '%bechamel%', '(?:(?:^|[^[:alpha:]])bechamel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bechamel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%beef bacon%', '%beef%', '(?:(?:^|[^[:alpha:]])beef[^[:alpha:]]+bacon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}beef[^[:alpha:]]+bacon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%beef%', '%beef%', '(?:(?:^|[^[:alpha:]])beef(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}beef(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:beefsteak(?:[''’]?s[''’]?)?[[:space:]-]+tomato)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%beer%', '%beer%', '(?:(?:^|[^[:alpha:]])beer(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}beer(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:ginger(?:[''’]?s[''’]?)?[[:space:]-]+beer|birch(?:[''’]?s[''’]?)?[[:space:]-]+beer|root(?:[''’]?s[''’]?)?[[:space:]-]+beer)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%beet%', '%beet%', '(?:(?:^|[^[:alpha:]])beet(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}beet(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bell pepper%', '%bell%', '(?:(?:^|[^[:alpha:]])bell[^[:alpha:]]+pepper(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bell[^[:alpha:]]+pepper(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%birch beer%', '%birch%', '(?:(?:^|[^[:alpha:]])birch[^[:alpha:]]+beer(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}birch[^[:alpha:]]+beer(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%biscuit%', '%biscuit%', '(?:(?:^|[^[:alpha:]])biscuit(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}biscuit(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bison%', '%bison%', '(?:(?:^|[^[:alpha:]])bison(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bison(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%black tea%', '%black%', '(?:(?:^|[^[:alpha:]])black[^[:alpha:]]+tea(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}black[^[:alpha:]]+tea(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%blue cheese%', '%blue%', '(?:(?:^|[^[:alpha:]])blue[^[:alpha:]]+cheese(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}blue[^[:alpha:]]+cheese(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bonito%', '%bonito%', '(?:(?:^|[^[:alpha:]])bonito(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bonito(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bottarga%', '%bottarga%', '(?:(?:^|[^[:alpha:]])bottarga(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bottarga(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bouillon%', '%bouillon%', '(?:(?:^|[^[:alpha:]])bouillon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bouillon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%brazil nut%', '%brazil%', '(?:(?:^|[^[:alpha:]])brazil[^[:alpha:]]+nut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}brazil[^[:alpha:]]+nut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bread%', '%bread%', '(?:(?:^|[^[:alpha:]])bread(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bread(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:sweetbread|breadfruit)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%breadcrumb%', '%breadcrumb%', '(?:(?:^|[^[:alpha:]])breadcrumb(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}breadcrumb(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%brie%', '%brie%', '(?:(?:^|[^[:alpha:]])brie(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}brie(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%brioche%', '%brioche%', '(?:(?:^|[^[:alpha:]])brioche(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}brioche(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%broad bean%', '%broad%', '(?:(?:^|[^[:alpha:]])broad[^[:alpha:]]+bean(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}broad[^[:alpha:]]+bean(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%broccoli%', '%broccoli%', '(?:(?:^|[^[:alpha:]])broccoli(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}broccoli(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%buckwheat%', '%buckwheat%', '(?:(?:^|[^[:alpha:]])buckwheat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}buckwheat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%bulgur%', '%bulgur%', '(?:(?:^|[^[:alpha:]])bulgur(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}bulgur(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%burrata%', '%burrata%', '(?:(?:^|[^[:alpha:]])burrata(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}burrata(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%butter%', '%butter%', '(?:(?:^|[^[:alpha:]])butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:sunflower(?:[''’]?s[''’]?)?[[:space:]-]+butter|butter(?:[''’]?s[''’]?)?[[:space:]-]+lettuce|peanut(?:[''’]?s[''’]?)?[[:space:]-]+butter|almond(?:[''’]?s[''’]?)?[[:space:]-]+butter|cashew(?:[''’]?s[''’]?)?[[:space:]-]+butter|apple(?:[''’]?s[''’]?)?[[:space:]-]+butter|cocoa(?:[''’]?s[''’]?)?[[:space:]-]+butter|cacao(?:[''’]?s[''’]?)?[[:space:]-]+butter|seed(?:[''’]?s[''’]?)?[[:space:]-]+butter|butter(?:[''’]?s[''’]?)?[[:space:]-]+bean|nut(?:[''’]?s[''’]?)?[[:space:]-]+butter|butterhead|butternut)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%cacao butter%', '%cacao%', '(?:(?:^|[^[:alpha:]])cacao[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cacao[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%caesar dressing%', '%caesar%', '(?:(?:^|[^[:alpha:]])caesar[^[:alpha:]]+dressing(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}caesar[^[:alpha:]]+dressing(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%caesar salad dressing%', '%caesar%', '(?:(?:^|[^[:alpha:]])caesar[^[:alpha:]]+salad[^[:alpha:]]+dressing(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}caesar[^[:alpha:]]+salad[^[:alpha:]]+dressing(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cajeta%', '%cajeta%', '(?:(?:^|[^[:alpha:]])cajeta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cajeta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%calamari%', '%calamari%', '(?:(?:^|[^[:alpha:]])calamari(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}calamari(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%camembert%', '%camembert%', '(?:(?:^|[^[:alpha:]])camembert(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}camembert(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%candy%', '%candy%', '(?:(?:^|[^[:alpha:]])candy(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}candy(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%carbonara%', '%carbonara%', '(?:(?:^|[^[:alpha:]])carbonara(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}carbonara(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%carrot%', '%carrot%', '(?:(?:^|[^[:alpha:]])carrot(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}carrot(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%casein%', '%casein%', '(?:(?:^|[^[:alpha:]])casein(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}casein(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cashew butter%', '%cashew%', '(?:(?:^|[^[:alpha:]])cashew[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cashew[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cashew%', '%cashew%', '(?:(?:^|[^[:alpha:]])cashew(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cashew(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%catfish%', '%catfish%', '(?:(?:^|[^[:alpha:]])catfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}catfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cauliflower%', '%cauliflower%', '(?:(?:^|[^[:alpha:]])cauliflower(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cauliflower(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%caviar%', '%caviar%', '(?:(?:^|[^[:alpha:]])caviar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}caviar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cayenne%', '%cayenne%', '(?:(?:^|[^[:alpha:]])cayenne(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cayenne(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%celeriac%', '%celeriac%', '(?:(?:^|[^[:alpha:]])celeriac(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}celeriac(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%celery%', '%celery%', '(?:(?:^|[^[:alpha:]])celery(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}celery(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%challah%', '%challah%', '(?:(?:^|[^[:alpha:]])challah(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}challah(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chard%', '%chard%', '(?:(?:^|[^[:alpha:]])chard(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chard(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cheddar%', '%cheddar%', '(?:(?:^|[^[:alpha:]])cheddar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cheddar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cheese%', '%cheese%', '(?:(?:^|[^[:alpha:]])cheese(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cheese(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:cheesecloth)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%chestnut%', '%chestnut%', '(?:(?:^|[^[:alpha:]])chestnut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chestnut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:water(?:[''’]?s[''’]?)?[[:space:]-]+chestnut)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%chicken%', '%chicken%', '(?:(?:^|[^[:alpha:]])chicken(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chicken(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chickpea%', '%chickpea%', '(?:(?:^|[^[:alpha:]])chickpea(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chickpea(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chile%', '%chile%', '(?:(?:^|[^[:alpha:]])chile(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chile(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:chilean)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%chili%', '%chili%', '(?:(?:^|[^[:alpha:]])chili(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chili(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chive%', '%chive%', '(?:(?:^|[^[:alpha:]])chive(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chive(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chocolate%', '%chocolate%', '(?:(?:^|[^[:alpha:]])chocolate(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chocolate(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%chorizo%', '%chorizo%', '(?:(?:^|[^[:alpha:]])chorizo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}chorizo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%citrus%', '%citrus%', '(?:(?:^|[^[:alpha:]])citrus(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}citrus(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%clam%', '%clam%', '(?:(?:^|[^[:alpha:]])clam(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}clam(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%clementine%', '%clementine%', '(?:(?:^|[^[:alpha:]])clementine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}clementine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cocoa butter%', '%cocoa%', '(?:(?:^|[^[:alpha:]])cocoa[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cocoa[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cocoa%', '%cocoa%', '(?:(?:^|[^[:alpha:]])cocoa(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cocoa(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%coconut%', '%coconut%', '(?:(?:^|[^[:alpha:]])coconut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}coconut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cod%', '%cod%', '(?:(?:^|[^[:alpha:]])cod(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cod(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:coddled|coddle)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%coffee%', '%coffee%', '(?:(?:^|[^[:alpha:]])coffee(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}coffee(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cola%', '%cola%', '(?:(?:^|[^[:alpha:]])cola(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cola(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:colander)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%colby%', '%colby%', '(?:(?:^|[^[:alpha:]])colby(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}colby(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%collard%', '%collard%', '(?:(?:^|[^[:alpha:]])collard(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}collard(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cookie%', '%cookie%', '(?:(?:^|[^[:alpha:]])cookie(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cookie(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%corn%', '%corn%', '(?:(?:^|[^[:alpha:]])corn(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}corn(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:peppercorn|cornish|corned)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%cotija%', '%cotija%', '(?:(?:^|[^[:alpha:]])cotija(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cotija(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cottage cheese%', '%cottage%', '(?:(?:^|[^[:alpha:]])cottage[^[:alpha:]]+cheese(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cottage[^[:alpha:]]+cheese(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%couscous%', '%couscous%', '(?:(?:^|[^[:alpha:]])couscous(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}couscous(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crab stick%', '%crab%', '(?:(?:^|[^[:alpha:]])crab[^[:alpha:]]+stick(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crab[^[:alpha:]]+stick(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crab%', '%crab%', '(?:(?:^|[^[:alpha:]])crab(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crab(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:crab(?:[''’]?s[''’]?)?[[:space:]-]+apple|crabapple)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%cracker%', '%cracker%', '(?:(?:^|[^[:alpha:]])cracker(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cracker(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cranberr%', '%cranberr%', '(?:(?:^|[^[:alpha:]])cranberr(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cranberr(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crawfish%', '%crawfish%', '(?:(?:^|[^[:alpha:]])crawfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crawfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crayfish tails%', '%crayfish%', '(?:(?:^|[^[:alpha:]])crayfish[^[:alpha:]]+tails(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crayfish[^[:alpha:]]+tails(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crayfish%', '%crayfish%', '(?:(?:^|[^[:alpha:]])crayfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crayfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cream cheese%', '%cream%', '(?:(?:^|[^[:alpha:]])cream[^[:alpha:]]+cheese(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cream[^[:alpha:]]+cheese(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cream%', '%cream%', '(?:(?:^|[^[:alpha:]])cream(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cream(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:cream(?:[''’]?s[''’]?)?[[:space:]-]+of(?:[''’]?s[''’]?)?[[:space:]-]+coconut|cream(?:[''’]?s[''’]?)?[[:space:]-]+of(?:[''’]?s[''’]?)?[[:space:]-]+tartar|coconut(?:[''’]?s[''’]?)?[[:space:]-]+cream)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%crepe%', '%crepe%', '(?:(?:^|[^[:alpha:]])crepe(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crepe(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%croissant%', '%croissant%', '(?:(?:^|[^[:alpha:]])croissant(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}croissant(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%crouton%', '%crouton%', '(?:(?:^|[^[:alpha:]])crouton(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}crouton(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%cupcake%', '%cupcake%', '(?:(?:^|[^[:alpha:]])cupcake(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}cupcake(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%custard%', '%custard%', '(?:(?:^|[^[:alpha:]])custard(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}custard(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:custard(?:[''’]?s[''’]?)?[[:space:]-]+apple)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%dark chocolate%', '%dark%', '(?:(?:^|[^[:alpha:]])dark[^[:alpha:]]+chocolate(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dark[^[:alpha:]]+chocolate(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%dashi%', '%dashi%', '(?:(?:^|[^[:alpha:]])dashi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dashi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%donut%', '%donut%', '(?:(?:^|[^[:alpha:]])donut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}donut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%doughnut%', '%doughnut%', '(?:(?:^|[^[:alpha:]])doughnut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}doughnut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%dried apricot%', '%dried%', '(?:(?:^|[^[:alpha:]])dried[^[:alpha:]]+apricot(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dried[^[:alpha:]]+apricot(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%dried fruit%', '%dried%', '(?:(?:^|[^[:alpha:]])dried[^[:alpha:]]+fruit(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dried[^[:alpha:]]+fruit(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%duck%', '%duck%', '(?:(?:^|[^[:alpha:]])duck(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}duck(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:duck(?:[''’]?s[''’]?)?[[:space:]-]+sauce)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%dukkah%', '%dukkah%', '(?:(?:^|[^[:alpha:]])dukkah(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dukkah(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%dulce de leche%', '%dulce%', '(?:(?:^|[^[:alpha:]])dulce[^[:alpha:]]+de[^[:alpha:]]+leche(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dulce[^[:alpha:]]+de[^[:alpha:]]+leche(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%dumpling%', '%dumpling%', '(?:(?:^|[^[:alpha:]])dumpling(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}dumpling(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%duqqa%', '%duqqa%', '(?:(?:^|[^[:alpha:]])duqqa(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}duqqa(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%edamame%', '%edamame%', '(?:(?:^|[^[:alpha:]])edamame(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}edamame(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%egg%', '%egg%', '(?:(?:^|[^[:alpha:]])egg(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}egg(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:eggplant)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%eggplant%', '%eggplant%', '(?:(?:^|[^[:alpha:]])eggplant(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}eggplant(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%emmental%', '%emmental%', '(?:(?:^|[^[:alpha:]])emmental(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}emmental(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%emmenthal%', '%emmenthal%', '(?:(?:^|[^[:alpha:]])emmenthal(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}emmenthal(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%energy drink%', '%energy%', '(?:(?:^|[^[:alpha:]])energy[^[:alpha:]]+drink(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}energy[^[:alpha:]]+drink(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%espresso%', '%espresso%', '(?:(?:^|[^[:alpha:]])espresso(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}espresso(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%farina%', '%farina%', '(?:(?:^|[^[:alpha:]])farina(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}farina(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%farro%', '%farro%', '(?:(?:^|[^[:alpha:]])farro(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}farro(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fava%', '%fava%', '(?:(?:^|[^[:alpha:]])fava(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fava(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%feta%', '%feta%', '(?:(?:^|[^[:alpha:]])feta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}feta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%filbert%', '%filbert%', '(?:(?:^|[^[:alpha:]])filbert(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}filbert(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fillo%', '%fillo%', '(?:(?:^|[^[:alpha:]])fillo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fillo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%filo%', '%filo%', '(?:(?:^|[^[:alpha:]])filo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}filo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fish%', '%fish%', '(?:(?:^|[^[:alpha:]])fish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:goldfish(?:[''’]?s[''’]?)?[[:space:]-]+cracker|swedish(?:[''’]?s[''’]?)?[[:space:]-]+fish|shellfish|crawfish|crayfish)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%flounder%', '%flounder%', '(?:(?:^|[^[:alpha:]])flounder(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}flounder(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%flour%', '%flour%', '(?:(?:^|[^[:alpha:]])flour(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}flour(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:buckwheat(?:[''’]?s[''’]?)?[[:space:]-]+flour|arrowroot(?:[''’]?s[''’]?)?[[:space:]-]+flour|chickpea(?:[''’]?s[''’]?)?[[:space:]-]+flour|coconut(?:[''’]?s[''’]?)?[[:space:]-]+flour|tapioca(?:[''’]?s[''’]?)?[[:space:]-]+flour|cassava(?:[''’]?s[''’]?)?[[:space:]-]+flour|almond(?:[''’]?s[''’]?)?[[:space:]-]+flour|potato(?:[''’]?s[''’]?)?[[:space:]-]+flour|corn(?:[''’]?s[''’]?)?[[:space:]-]+flour|rice(?:[''’]?s[''’]?)?[[:space:]-]+flour|cornflour|oat(?:[''’]?s[''’]?)?[[:space:]-]+flour)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%fontina%', '%fontina%', '(?:(?:^|[^[:alpha:]])fontina(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fontina(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fortified%', '%fortified%', '(?:(?:^|[^[:alpha:]])fortified(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fortified(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%frangipane%', '%frangipane%', '(?:(?:^|[^[:alpha:]])frangipane(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}frangipane(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fried%', '%fried%', '(?:(?:^|[^[:alpha:]])fried(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fried(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%frittata%', '%frittata%', '(?:(?:^|[^[:alpha:]])frittata(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}frittata(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%frosting%', '%frosting%', '(?:(?:^|[^[:alpha:]])frosting(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}frosting(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%fruit%', '%fruit%', '(?:(?:^|[^[:alpha:]])fruit(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}fruit(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%furikake%', '%furikake%', '(?:(?:^|[^[:alpha:]])furikake(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}furikake(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%garlic%', '%garlic%', '(?:(?:^|[^[:alpha:]])garlic(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}garlic(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gelatin%', '%gelatin%', '(?:(?:^|[^[:alpha:]])gelatin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gelatin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ghee%', '%ghee%', '(?:(?:^|[^[:alpha:]])ghee(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ghee(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gianduia%', '%gianduia%', '(?:(?:^|[^[:alpha:]])gianduia(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gianduia(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gianduja%', '%gianduja%', '(?:(?:^|[^[:alpha:]])gianduja(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gianduja(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ginger beer%', '%ginger%', '(?:(?:^|[^[:alpha:]])ginger[^[:alpha:]]+beer(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ginger[^[:alpha:]]+beer(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ginger%', '%ginger%', '(?:(?:^|[^[:alpha:]])ginger(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ginger(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gnocchi%', '%gnocchi%', '(?:(?:^|[^[:alpha:]])gnocchi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gnocchi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%goat%', '%goat%', '(?:(?:^|[^[:alpha:]])goat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}goat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:goat(?:[''’]?s[''’]?)?[[:space:]-]+cheese|goat(?:[''’]?s[''’]?)?[[:space:]-]+yogurt|goat(?:[''’]?s[''’]?)?[[:space:]-]+milk)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%goji%', '%goji%', '(?:(?:^|[^[:alpha:]])goji(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}goji(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%goldfish cracker%', '%goldfish%', '(?:(?:^|[^[:alpha:]])goldfish[^[:alpha:]]+cracker(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}goldfish[^[:alpha:]]+cracker(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%goldfish%', '%goldfish%', '(?:(?:^|[^[:alpha:]])goldfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}goldfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gomasio%', '%gomasio%', '(?:(?:^|[^[:alpha:]])gomasio(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gomasio(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%goose%', '%goose%', '(?:(?:^|[^[:alpha:]])goose(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}goose(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:gooseberr(?:y|ies))(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%gorgonzola%', '%gorgonzola%', '(?:(?:^|[^[:alpha:]])gorgonzola(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gorgonzola(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gouda%', '%gouda%', '(?:(?:^|[^[:alpha:]])gouda(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gouda(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%grain%', '%grain%', '(?:(?:^|[^[:alpha:]])grain(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}grain(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%granola%', '%granola%', '(?:(?:^|[^[:alpha:]])granola(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}granola(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%grapefruit%', '%grapefruit%', '(?:(?:^|[^[:alpha:]])grapefruit(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}grapefruit(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%green tea%', '%green%', '(?:(?:^|[^[:alpha:]])green[^[:alpha:]]+tea(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}green[^[:alpha:]]+tea(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%grits%', '%grits%', '(?:(?:^|[^[:alpha:]])grits(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}grits(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ground meat%', '%ground%', '(?:(?:^|[^[:alpha:]])ground[^[:alpha:]]+meat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ground[^[:alpha:]]+meat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%groundnut%', '%groundnut%', '(?:(?:^|[^[:alpha:]])groundnut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}groundnut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gruyere%', '%gruyere%', '(?:(?:^|[^[:alpha:]])gruyere(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gruyere(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%guacamole%', '%guacamole%', '(?:(?:^|[^[:alpha:]])guacamole(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}guacamole(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%gyoza wrappers%', '%gyoza%', '(?:(?:^|[^[:alpha:]])gyoza[^[:alpha:]]+wrappers(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}gyoza[^[:alpha:]]+wrappers(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%haddock%', '%haddock%', '(?:(?:^|[^[:alpha:]])haddock(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}haddock(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%halibut%', '%halibut%', '(?:(?:^|[^[:alpha:]])halibut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}halibut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%halloumi%', '%halloumi%', '(?:(?:^|[^[:alpha:]])halloumi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}halloumi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%halva%', '%halva%', '(?:(?:^|[^[:alpha:]])halva(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}halva(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ham%', '%ham%', '(?:(?:^|[^[:alpha:]])ham(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ham(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:hamantaschen|hamburger|graham)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%hamburger%', '%hamburger%', '(?:(?:^|[^[:alpha:]])hamburger(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hamburger(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:hamburger(?:[''’]?s[''’]?)?[[:space:]-]+roll|hamburger(?:[''’]?s[''’]?)?[[:space:]-]+bun)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%havarti%', '%havarti%', '(?:(?:^|[^[:alpha:]])havarti(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}havarti(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hazelnut spread%', '%hazelnut%', '(?:(?:^|[^[:alpha:]])hazelnut[^[:alpha:]]+spread(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hazelnut[^[:alpha:]]+spread(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hazelnut%', '%hazelnut%', '(?:(?:^|[^[:alpha:]])hazelnut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hazelnut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%heavy cream%', '%heavy%', '(?:(?:^|[^[:alpha:]])heavy[^[:alpha:]]+cream(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}heavy[^[:alpha:]]+cream(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%herring%', '%herring%', '(?:(?:^|[^[:alpha:]])herring(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}herring(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hoisin%', '%hoisin%', '(?:(?:^|[^[:alpha:]])hoisin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hoisin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hollandaise%', '%hollandaise%', '(?:(?:^|[^[:alpha:]])hollandaise(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hollandaise(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%honey%', '%honey%', '(?:(?:^|[^[:alpha:]])honey(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}honey(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:honeycrisp|honeydew)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%honeycrisp%', '%honeycrisp%', '(?:(?:^|[^[:alpha:]])honeycrisp(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}honeycrisp(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hot dog%', '%hot%', '(?:(?:^|[^[:alpha:]])hot[^[:alpha:]]+dog(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hot[^[:alpha:]]+dog(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:hot(?:[''’]?s[''’]?)?[[:space:]-]+dog(?:[''’]?s[''’]?)?[[:space:]-]+roll|hot(?:[''’]?s[''’]?)?[[:space:]-]+dog(?:[''’]?s[''’]?)?[[:space:]-]+bun)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%houmous%', '%houmous%', '(?:(?:^|[^[:alpha:]])houmous(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}houmous(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%hummus%', '%hummus%', '(?:(?:^|[^[:alpha:]])hummus(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}hummus(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ice cream%', '%ice%', '(?:(?:^|[^[:alpha:]])ice[^[:alpha:]]+cream(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ice[^[:alpha:]]+cream(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%icing%', '%icing%', '(?:(?:^|[^[:alpha:]])icing(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}icing(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%imitation crab%', '%imitation%', '(?:(?:^|[^[:alpha:]])imitation[^[:alpha:]]+crab(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}imitation[^[:alpha:]]+crab(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%jalape%', '%jalape%', '(?:(?:^|[^[:alpha:]])jalape(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}jalape(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%jam%', '%jam%', '(?:(?:^|[^[:alpha:]])jam(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}jam(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:jambalaya|jamaican)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%jelly%', '%jelly%', '(?:(?:^|[^[:alpha:]])jelly(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}jelly(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%kale%', '%kale%', '(?:(?:^|[^[:alpha:]])kale(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}kale(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%kamut%', '%kamut%', '(?:(?:^|[^[:alpha:]])kamut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}kamut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%katsuobushi%', '%katsuobushi%', '(?:(?:^|[^[:alpha:]])katsuobushi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}katsuobushi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%kefir%', '%kefir%', '(?:(?:^|[^[:alpha:]])kefir(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}kefir(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%kimchi%', '%kimchi%', '(?:(?:^|[^[:alpha:]])kimchi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}kimchi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%kiwi%', '%kiwi%', '(?:(?:^|[^[:alpha:]])kiwi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}kiwi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%labne%', '%labne%', '(?:(?:^|[^[:alpha:]])labne(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}labne(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lamb%', '%lamb%', '(?:(?:^|[^[:alpha:]])lamb(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lamb(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:lamb(?:[''’]?s[''’]?)?[[:space:]-]+lettuce|lambrusco)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%langoustine%', '%langoustine%', '(?:(?:^|[^[:alpha:]])langoustine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}langoustine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lard%', '%lard%', '(?:(?:^|[^[:alpha:]])lard(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lard(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:collard)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%lassi%', '%lassi%', '(?:(?:^|[^[:alpha:]])lassi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lassi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%leek%', '%leek%', '(?:(?:^|[^[:alpha:]])leek(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}leek(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lemon%', '%lemon%', '(?:(?:^|[^[:alpha:]])lemon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lemon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:lemon(?:[''’]?s[''’]?)?[[:space:]-]+verbena|lemon(?:[''’]?s[''’]?)?[[:space:]-]+thyme|lemongrass|lemon(?:[''’]?s[''’]?)?[[:space:]-]+balm)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%lentil%', '%lentil%', '(?:(?:^|[^[:alpha:]])lentil(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lentil(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%licorice%', '%licorice%', '(?:(?:^|[^[:alpha:]])licorice(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}licorice(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lime%', '%lime%', '(?:(?:^|[^[:alpha:]])lime(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lime(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%liquorice%', '%liquorice%', '(?:(?:^|[^[:alpha:]])liquorice(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}liquorice(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%liver%', '%liver%', '(?:(?:^|[^[:alpha:]])liver(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}liver(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lobster%', '%lobster%', '(?:(?:^|[^[:alpha:]])lobster(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lobster(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:lobster(?:[''’]?s[''’]?)?[[:space:]-]+mushroom)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%lox%', '%lox%', '(?:(?:^|[^[:alpha:]])lox(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lox(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%lupin%', '%lupin%', '(?:(?:^|[^[:alpha:]])lupin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}lupin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%macadamia%', '%macadamia%', '(?:(?:^|[^[:alpha:]])macadamia(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}macadamia(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mackerel%', '%mackerel%', '(?:(?:^|[^[:alpha:]])mackerel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mackerel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mahi%', '%mahi%', '(?:(?:^|[^[:alpha:]])mahi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mahi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%maize%', '%maize%', '(?:(?:^|[^[:alpha:]])maize(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}maize(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%malt%', '%malt%', '(?:(?:^|[^[:alpha:]])malt(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}malt(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:maltodextrin)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%maltodextrin%', '%maltodextrin%', '(?:(?:^|[^[:alpha:]])maltodextrin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}maltodextrin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%manchego%', '%manchego%', '(?:(?:^|[^[:alpha:]])manchego(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}manchego(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mango%', '%mango%', '(?:(?:^|[^[:alpha:]])mango(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mango(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:mangosteen)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%marzipan%', '%marzipan%', '(?:(?:^|[^[:alpha:]])marzipan(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}marzipan(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mascarpone%', '%mascarpone%', '(?:(?:^|[^[:alpha:]])mascarpone(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mascarpone(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%matcha%', '%matcha%', '(?:(?:^|[^[:alpha:]])matcha(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}matcha(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%matzah%', '%matzah%', '(?:(?:^|[^[:alpha:]])matzah(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}matzah(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%matzo%', '%matzo%', '(?:(?:^|[^[:alpha:]])matzo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}matzo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mayo%', '%mayo%', '(?:(?:^|[^[:alpha:]])mayo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mayo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mayonnaise%', '%mayonnaise%', '(?:(?:^|[^[:alpha:]])mayonnaise(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mayonnaise(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%meatball%', '%meatball%', '(?:(?:^|[^[:alpha:]])meatball(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}meatball(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%meringue%', '%meringue%', '(?:(?:^|[^[:alpha:]])meringue(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}meringue(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%milk%', '%milk%', '(?:(?:^|[^[:alpha:]])milk(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}milk(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:coconut(?:[''’]?s[''’]?)?[[:space:]-]+milk|milk(?:[''’]?s[''’]?)?[[:space:]-]+thistle|almond(?:[''’]?s[''’]?)?[[:space:]-]+milk|cashew(?:[''’]?s[''’]?)?[[:space:]-]+milk|rice(?:[''’]?s[''’]?)?[[:space:]-]+milk|soy(?:[''’]?s[''’]?)?[[:space:]-]+milk|oat(?:[''’]?s[''’]?)?[[:space:]-]+milk|milkweed|milkfish|soymilk)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%millet%', '%millet%', '(?:(?:^|[^[:alpha:]])millet(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}millet(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%miso%', '%miso%', '(?:(?:^|[^[:alpha:]])miso(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}miso(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mixed nut%', '%mixed%', '(?:(?:^|[^[:alpha:]])mixed[^[:alpha:]]+nut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mixed[^[:alpha:]]+nut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%monosodium glutamate%', '%monosodium%', '(?:(?:^|[^[:alpha:]])monosodium[^[:alpha:]]+glutamate(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}monosodium[^[:alpha:]]+glutamate(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%monterey jack%', '%monterey%', '(?:(?:^|[^[:alpha:]])monterey[^[:alpha:]]+jack(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}monterey[^[:alpha:]]+jack(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mozzarella%', '%mozzarella%', '(?:(?:^|[^[:alpha:]])mozzarella(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mozzarella(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%msg%', '%msg%', '(?:(?:^|[^[:alpha:]])msg(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}msg(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%muenster%', '%muenster%', '(?:(?:^|[^[:alpha:]])muenster(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}muenster(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%muesli%', '%muesli%', '(?:(?:^|[^[:alpha:]])muesli(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}muesli(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%muffin%', '%muffin%', '(?:(?:^|[^[:alpha:]])muffin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}muffin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%munster%', '%munster%', '(?:(?:^|[^[:alpha:]])munster(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}munster(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mushroom%', '%mushroom%', '(?:(?:^|[^[:alpha:]])mushroom(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mushroom(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mussel%', '%mussel%', '(?:(?:^|[^[:alpha:]])mussel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mussel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%mustard%', '%mustard%', '(?:(?:^|[^[:alpha:]])mustard(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}mustard(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%naan%', '%naan%', '(?:(?:^|[^[:alpha:]])naan(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}naan(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%nan bread%', '%nan%', '(?:(?:^|[^[:alpha:]])nan[^[:alpha:]]+bread(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}nan[^[:alpha:]]+bread(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%natto%', '%natto%', '(?:(?:^|[^[:alpha:]])natto(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}natto(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%neufchatel%', '%neufchatel%', '(?:(?:^|[^[:alpha:]])neufchatel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}neufchatel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%noodle%', '%noodle%', '(?:(?:^|[^[:alpha:]])noodle(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}noodle(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%nougat%', '%nougat%', '(?:(?:^|[^[:alpha:]])nougat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}nougat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%nut butter%', '%nut%', '(?:(?:^|[^[:alpha:]])nut[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}nut[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:coconut(?:[''’]?s[''’]?)?[[:space:]-]+butter|peanut(?:[''’]?s[''’]?)?[[:space:]-]+butter)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%nutella%', '%nutella%', '(?:(?:^|[^[:alpha:]])nutella(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}nutella(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%oat milk%', '%oat%', '(?:(?:^|[^[:alpha:]])oat[^[:alpha:]]+milk(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}oat[^[:alpha:]]+milk(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%oat%', '%oat%', '(?:(?:^|[^[:alpha:]])oat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}oat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%octopus%', '%octopus%', '(?:(?:^|[^[:alpha:]])octopus(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}octopus(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%onion%', '%onion%', '(?:(?:^|[^[:alpha:]])onion(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}onion(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%orange%', '%orange%', '(?:(?:^|[^[:alpha:]])orange(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}orange(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:orange(?:[''’]?s[''’]?)?[[:space:]-]+rough(?:y|ies))(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%orgeat%', '%orgeat%', '(?:(?:^|[^[:alpha:]])orgeat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}orgeat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%orzo%', '%orzo%', '(?:(?:^|[^[:alpha:]])orzo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}orzo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%oyster%', '%oyster%', '(?:(?:^|[^[:alpha:]])oyster(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}oyster(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:oyster(?:[''’]?s[''’]?)?[[:space:]-]+mushroom|oyster(?:[''’]?s[''’]?)?[[:space:]-]+cracker)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%pancake%', '%pancake%', '(?:(?:^|[^[:alpha:]])pancake(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pancake(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pancetta%', '%pancetta%', '(?:(?:^|[^[:alpha:]])pancetta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pancetta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%paneer%', '%paneer%', '(?:(?:^|[^[:alpha:]])paneer(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}paneer(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%panko%', '%panko%', '(?:(?:^|[^[:alpha:]])panko(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}panko(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%paprika%', '%paprika%', '(?:(?:^|[^[:alpha:]])paprika(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}paprika(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%parmesan%', '%parmesan%', '(?:(?:^|[^[:alpha:]])parmesan(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}parmesan(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pasta%', '%pasta%', '(?:(?:^|[^[:alpha:]])pasta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pasta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pastries%', '%pastries%', '(?:(?:^|[^[:alpha:]])pastries(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pastries(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pastry%', '%pastry%', '(?:(?:^|[^[:alpha:]])pastry(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pastry(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%peanut butter%', '%peanut%', '(?:(?:^|[^[:alpha:]])peanut[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}peanut[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%peanut%', '%peanut%', '(?:(?:^|[^[:alpha:]])peanut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}peanut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pear%', '%pear%', '(?:(?:^|[^[:alpha:]])pear(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pear(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:pearl)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%pearl barley%', '%pearl%', '(?:(?:^|[^[:alpha:]])pearl[^[:alpha:]]+barley(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pearl[^[:alpha:]]+barley(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pecan%', '%pecan%', '(?:(?:^|[^[:alpha:]])pecan(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pecan(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pecorino%', '%pecorino%', '(?:(?:^|[^[:alpha:]])pecorino(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pecorino(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pepper jack%', '%pepper%', '(?:(?:^|[^[:alpha:]])pepper[^[:alpha:]]+jack(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pepper[^[:alpha:]]+jack(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pepperoni%', '%pepperoni%', '(?:(?:^|[^[:alpha:]])pepperoni(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pepperoni(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pesto%', '%pesto%', '(?:(?:^|[^[:alpha:]])pesto(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pesto(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%phyllo%', '%phyllo%', '(?:(?:^|[^[:alpha:]])phyllo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}phyllo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pickle%', '%pickle%', '(?:(?:^|[^[:alpha:]])pickle(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pickle(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pie crust%', '%pie%', '(?:(?:^|[^[:alpha:]])pie[^[:alpha:]]+crust(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pie[^[:alpha:]]+crust(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pie dough%', '%pie%', '(?:(?:^|[^[:alpha:]])pie[^[:alpha:]]+dough(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pie[^[:alpha:]]+dough(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pie shell%', '%pie%', '(?:(?:^|[^[:alpha:]])pie[^[:alpha:]]+shell(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pie[^[:alpha:]]+shell(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pine nut%', '%pine%', '(?:(?:^|[^[:alpha:]])pine[^[:alpha:]]+nut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pine[^[:alpha:]]+nut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pistachio%', '%pistachio%', '(?:(?:^|[^[:alpha:]])pistachio(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pistachio(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pita%', '%pita%', '(?:(?:^|[^[:alpha:]])pita(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pita(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%plantain%', '%plantain%', '(?:(?:^|[^[:alpha:]])plantain(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}plantain(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%polenta%', '%polenta%', '(?:(?:^|[^[:alpha:]])polenta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}polenta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pollock%', '%pollock%', '(?:(?:^|[^[:alpha:]])pollock(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pollock(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ponzu%', '%ponzu%', '(?:(?:^|[^[:alpha:]])ponzu(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ponzu(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%popcorn%', '%popcorn%', '(?:(?:^|[^[:alpha:]])popcorn(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}popcorn(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pork%', '%pork%', '(?:(?:^|[^[:alpha:]])pork(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pork(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%potato%', '%potato%', '(?:(?:^|[^[:alpha:]])potato(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}potato(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%potsticker wrappers%', '%potsticker%', '(?:(?:^|[^[:alpha:]])potsticker[^[:alpha:]]+wrappers(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}potsticker[^[:alpha:]]+wrappers(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%poultry%', '%poultry%', '(?:(?:^|[^[:alpha:]])poultry(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}poultry(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:poultry(?:[''’]?s[''’]?)?[[:space:]-]+seasoning)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%praline%', '%praline%', '(?:(?:^|[^[:alpha:]])praline(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}praline(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%prawn%', '%prawn%', '(?:(?:^|[^[:alpha:]])prawn(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}prawn(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%pretzel%', '%pretzel%', '(?:(?:^|[^[:alpha:]])pretzel(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}pretzel(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%prosciutto%', '%prosciutto%', '(?:(?:^|[^[:alpha:]])prosciutto(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}prosciutto(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%provolone%', '%provolone%', '(?:(?:^|[^[:alpha:]])provolone(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}provolone(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%quail%', '%quail%', '(?:(?:^|[^[:alpha:]])quail(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}quail(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%queso%', '%queso%', '(?:(?:^|[^[:alpha:]])queso(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}queso(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%quiche%', '%quiche%', '(?:(?:^|[^[:alpha:]])quiche(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}quiche(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%quinoa%', '%quinoa%', '(?:(?:^|[^[:alpha:]])quinoa(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}quinoa(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%radish%', '%radish%', '(?:(?:^|[^[:alpha:]])radish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}radish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%raisin%', '%raisin%', '(?:(?:^|[^[:alpha:]])raisin(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}raisin(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%raita%', '%raita%', '(?:(?:^|[^[:alpha:]])raita(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}raita(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ramen%', '%ramen%', '(?:(?:^|[^[:alpha:]])ramen(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ramen(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ranch dressing%', '%ranch%', '(?:(?:^|[^[:alpha:]])ranch[^[:alpha:]]+dressing(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ranch[^[:alpha:]]+dressing(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%ranch salad dressing%', '%ranch%', '(?:(?:^|[^[:alpha:]])ranch[^[:alpha:]]+salad[^[:alpha:]]+dressing(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ranch[^[:alpha:]]+salad[^[:alpha:]]+dressing(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%rhubarb%', '%rhubarb%', '(?:(?:^|[^[:alpha:]])rhubarb(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}rhubarb(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%rice%', '%rice%', '(?:(?:^|[^[:alpha:]])rice(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}rice(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:liquorice|licorice)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%ricotta%', '%ricotta%', '(?:(?:^|[^[:alpha:]])ricotta(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}ricotta(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%romesco%', '%romesco%', '(?:(?:^|[^[:alpha:]])romesco(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}romesco(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%root beer%', '%root%', '(?:(?:^|[^[:alpha:]])root[^[:alpha:]]+beer(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}root[^[:alpha:]]+beer(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%roquefort%', '%roquefort%', '(?:(?:^|[^[:alpha:]])roquefort(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}roquefort(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%roux%', '%roux%', '(?:(?:^|[^[:alpha:]])roux(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}roux(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%rye%', '%rye%', '(?:(?:^|[^[:alpha:]])rye(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}rye(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sabayon%', '%sabayon%', '(?:(?:^|[^[:alpha:]])sabayon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sabayon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%salami%', '%salami%', '(?:(?:^|[^[:alpha:]])salami(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}salami(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%salmon%', '%salmon%', '(?:(?:^|[^[:alpha:]])salmon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}salmon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:salmonberr(?:y|ies))(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%sardine%', '%sardine%', '(?:(?:^|[^[:alpha:]])sardine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sardine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%satay%', '%satay%', '(?:(?:^|[^[:alpha:]])satay(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}satay(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sauerkraut%', '%sauerkraut%', '(?:(?:^|[^[:alpha:]])sauerkraut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sauerkraut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sausage%', '%sausage%', '(?:(?:^|[^[:alpha:]])sausage(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sausage(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%scallop%', '%scallop%', '(?:(?:^|[^[:alpha:]])scallop(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}scallop(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:scallopini|scalloped)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%scallopini%', '%scallopini%', '(?:(?:^|[^[:alpha:]])scallopini(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}scallopini(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%scampi%', '%scampi%', '(?:(?:^|[^[:alpha:]])scampi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}scampi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%seed butter%', '%seed%', '(?:(?:^|[^[:alpha:]])seed[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}seed[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%seitan%', '%seitan%', '(?:(?:^|[^[:alpha:]])seitan(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}seitan(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%semolina%', '%semolina%', '(?:(?:^|[^[:alpha:]])semolina(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}semolina(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sesame%', '%sesame%', '(?:(?:^|[^[:alpha:]])sesame(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sesame(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%shallot%', '%shallot%', '(?:(?:^|[^[:alpha:]])shallot(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}shallot(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%shellfish%', '%shellfish%', '(?:(?:^|[^[:alpha:]])shellfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}shellfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%shoyu%', '%shoyu%', '(?:(?:^|[^[:alpha:]])shoyu(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}shoyu(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%shrimp%', '%shrimp%', '(?:(?:^|[^[:alpha:]])shrimp(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}shrimp(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%snapper%', '%snapper%', '(?:(?:^|[^[:alpha:]])snapper(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}snapper(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soba%', '%soba%', '(?:(?:^|[^[:alpha:]])soba(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soba(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soda%', '%soda%', '(?:(?:^|[^[:alpha:]])soda(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soda(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:bicarbonate(?:[''’]?s[''’]?)?[[:space:]-]+of(?:[''’]?s[''’]?)?[[:space:]-]+soda|baking(?:[''’]?s[''’]?)?[[:space:]-]+soda|soda(?:[''’]?s[''’]?)?[[:space:]-]+water|club(?:[''’]?s[''’]?)?[[:space:]-]+soda)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%sole%', '%sole%', '(?:(?:^|[^[:alpha:]])sole(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sole(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sorghum%', '%sorghum%', '(?:(?:^|[^[:alpha:]])sorghum(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sorghum(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%souffle%', '%souffle%', '(?:(?:^|[^[:alpha:]])souffle(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}souffle(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soy milk%', '%soy%', '(?:(?:^|[^[:alpha:]])soy[^[:alpha:]]+milk(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soy[^[:alpha:]]+milk(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soy sauce%', '%soy%', '(?:(?:^|[^[:alpha:]])soy[^[:alpha:]]+sauce(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soy[^[:alpha:]]+sauce(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soy yogurt%', '%soy%', '(?:(?:^|[^[:alpha:]])soy[^[:alpha:]]+yogurt(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soy[^[:alpha:]]+yogurt(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soy%', '%soy%', '(?:(?:^|[^[:alpha:]])soy(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soy(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soya sauce%', '%soya%', '(?:(?:^|[^[:alpha:]])soya[^[:alpha:]]+sauce(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soya[^[:alpha:]]+sauce(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%soymilk%', '%soymilk%', '(?:(?:^|[^[:alpha:]])soymilk(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}soymilk(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%spaghetti%', '%spaghetti%', '(?:(?:^|[^[:alpha:]])spaghetti(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}spaghetti(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:spaghetti(?:[''’]?s[''’]?)?[[:space:]-]+squash)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%spelt%', '%spelt%', '(?:(?:^|[^[:alpha:]])spelt(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}spelt(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%spinach%', '%spinach%', '(?:(?:^|[^[:alpha:]])spinach(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}spinach(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%squid%', '%squid%', '(?:(?:^|[^[:alpha:]])squid(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}squid(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%steak%', '%steak%', '(?:(?:^|[^[:alpha:]])steak(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}steak(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:steak(?:[''’]?s[''’]?)?[[:space:]-]+seasoning|steak(?:[''’]?s[''’]?)?[[:space:]-]+sauce)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%strawberr%', '%strawberr%', '(?:(?:^|[^[:alpha:]])strawberr(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}strawberr(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sugar%', '%sugar%', '(?:(?:^|[^[:alpha:]])sugar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sugar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sunflower butter%', '%sunflower%', '(?:(?:^|[^[:alpha:]])sunflower[^[:alpha:]]+butter(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sunflower[^[:alpha:]]+butter(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%surimi%', '%surimi%', '(?:(?:^|[^[:alpha:]])surimi(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}surimi(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sweet potato%', '%sweet%', '(?:(?:^|[^[:alpha:]])sweet[^[:alpha:]]+potato(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sweet[^[:alpha:]]+potato(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%sweetbread%', '%sweetbread%', '(?:(?:^|[^[:alpha:]])sweetbread(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}sweetbread(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%swordfish%', '%swordfish%', '(?:(?:^|[^[:alpha:]])swordfish(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}swordfish(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%syrup%', '%syrup%', '(?:(?:^|[^[:alpha:]])syrup(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}syrup(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tahina%', '%tahina%', '(?:(?:^|[^[:alpha:]])tahina(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tahina(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tahini%', '%tahini%', '(?:(?:^|[^[:alpha:]])tahini(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tahini(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tamari%', '%tamari%', '(?:(?:^|[^[:alpha:]])tamari(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tamari(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:tamarind)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%tangerine%', '%tangerine%', '(?:(?:^|[^[:alpha:]])tangerine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tangerine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tart shell%', '%tart%', '(?:(?:^|[^[:alpha:]])tart[^[:alpha:]]+shell(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tart[^[:alpha:]]+shell(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tempeh%', '%tempeh%', '(?:(?:^|[^[:alpha:]])tempeh(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tempeh(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tempura%', '%tempura%', '(?:(?:^|[^[:alpha:]])tempura(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tempura(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%teriyaki%', '%teriyaki%', '(?:(?:^|[^[:alpha:]])teriyaki(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}teriyaki(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tilapia%', '%tilapia%', '(?:(?:^|[^[:alpha:]])tilapia(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tilapia(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tofu%', '%tofu%', '(?:(?:^|[^[:alpha:]])tofu(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tofu(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tomatillo%', '%tomatillo%', '(?:(?:^|[^[:alpha:]])tomatillo(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tomatillo(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tomato%', '%tomato%', '(?:(?:^|[^[:alpha:]])tomato(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tomato(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%torrone%', '%torrone%', '(?:(?:^|[^[:alpha:]])torrone(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}torrone(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tortilla%', '%tortilla%', '(?:(?:^|[^[:alpha:]])tortilla(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tortilla(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:flour(?:[''’]?s[''’]?)?[[:space:]-]+tortilla|corn(?:[''’]?s[''’]?)?[[:space:]-]+tortilla)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%tree nut%', '%tree%', '(?:(?:^|[^[:alpha:]])tree[^[:alpha:]]+nut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tree[^[:alpha:]]+nut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%triticale%', '%triticale%', '(?:(?:^|[^[:alpha:]])triticale(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}triticale(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%trout%', '%trout%', '(?:(?:^|[^[:alpha:]])trout(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}trout(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tuna%', '%tuna%', '(?:(?:^|[^[:alpha:]])tuna(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tuna(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%turkey bacon%', '%turkey%', '(?:(?:^|[^[:alpha:]])turkey[^[:alpha:]]+bacon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}turkey[^[:alpha:]]+bacon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%turkey%', '%turkey%', '(?:(?:^|[^[:alpha:]])turkey(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}turkey(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%turnip%', '%turnip%', '(?:(?:^|[^[:alpha:]])turnip(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}turnip(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%tzatziki%', '%tzatziki%', '(?:(?:^|[^[:alpha:]])tzatziki(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}tzatziki(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%udon%', '%udon%', '(?:(?:^|[^[:alpha:]])udon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}udon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%veal%', '%veal%', '(?:(?:^|[^[:alpha:]])veal(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}veal(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%vegetable%', '%vegetable%', '(?:(?:^|[^[:alpha:]])vegetable(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}vegetable(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%venison%', '%venison%', '(?:(?:^|[^[:alpha:]])venison(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}venison(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%vinegar%', '%vinegar%', '(?:(?:^|[^[:alpha:]])vinegar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}vinegar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%waffle%', '%waffle%', '(?:(?:^|[^[:alpha:]])waffle(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}waffle(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%walnut%', '%walnut%', '(?:(?:^|[^[:alpha:]])walnut(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}walnut(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%watermelon%', '%watermelon%', '(?:(?:^|[^[:alpha:]])watermelon(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}watermelon(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%wheat%', '%wheat%', '(?:(?:^|[^[:alpha:]])wheat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}wheat(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:buckwheat)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%whey%', '%whey%', '(?:(?:^|[^[:alpha:]])whey(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}whey(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%wine%', '%wine%', '(?:(?:^|[^[:alpha:]])wine(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}wine(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%wonton%', '%wonton%', '(?:(?:^|[^[:alpha:]])wonton(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}wonton(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%worcestershire%', '%worcestershire%', '(?:(?:^|[^[:alpha:]])worcestershire(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}worcestershire(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%worchestershire sauce%', '%worchestershire%', '(?:(?:^|[^[:alpha:]])worchestershire[^[:alpha:]]+sauce(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}worchestershire[^[:alpha:]]+sauce(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%yerba%', '%yerba%', '(?:(?:^|[^[:alpha:]])yerba(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}yerba(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%yoghurt%', '%yoghurt%', '(?:(?:^|[^[:alpha:]])yoghurt(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}yoghurt(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:coconut(?:[''’]?s[''’]?)?[[:space:]-]+yoghurt|soy(?:[''’]?s[''’]?)?[[:space:]-]+yoghurt)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%yogurt%', '%yogurt%', '(?:(?:^|[^[:alpha:]])yogurt(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}yogurt(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', '(?:^|[^[:alpha:]])(?:coconut(?:[''’]?s[''’]?)?[[:space:]-]+yogurt|almond(?:[''’]?s[''’]?)?[[:space:]-]+yogurt|soy(?:[''’]?s[''’]?)?[[:space:]-]+yogurt)(?:s|es|[''’]s)?(?![[:alpha:]])'),
        ('%yuzu%', '%yuzu%', '(?:(?:^|[^[:alpha:]])yuzu(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}yuzu(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%za''atar%', '%za%', '(?:(?:^|[^[:alpha:]])za[^[:alpha:]]+atar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}za[^[:alpha:]]+atar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%zaatar%', '%zaatar%', '(?:(?:^|[^[:alpha:]])zaatar(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}zaatar(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('%zabaglione%', '%zabaglione%', '(?:(?:^|[^[:alpha:]])zabaglione(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|[[:alpha:]]{3}zabaglione(?:(?:s|es|ed|ing)?(?![[:alpha:]])(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$))|(?!less)[[:alpha:]]{3}))', NULL),
        ('oat%', '%oat%', '(?:^oat(?!less)(?![- ]?free(?![- ]?(?:range|run|roam))(?:[^[:alpha:]]|$)))', NULL)
),
named AS (
    SELECT r."Id" AS ref_id, r."Name" AS type_name
    FROM reference."Reference" r
    WHERE NOT r."IsDeleted"
      AND ((:'names' = '' AND EXISTS (SELECT 1 FROM plan."RestrictionCriterion" c WHERE c."RestrictionTypeId" = r."Id" AND NOT c."IsDeleted"))
           OR r."Name" = ANY (string_to_array(:'names', ','))
           OR r."Id" IN (SELECT x."RestrictionTypeId" FROM plan."Restriction" x WHERE x."RestrictionTypeId" IS NOT NULL AND NOT x."IsDeleted"))
),
type_groups AS (
    SELECT min(ref_id) AS type_id, type_name, string_agg(ref_id::text, ',' ORDER BY ref_id) AS ref_ids
    FROM named
    GROUP BY type_name
),
types AS (
    SELECT n.ref_id, g.type_id, g.type_name
    FROM named n
    JOIN type_groups g ON g.type_name = n.type_name
),
all_crit AS (
    SELECT t.type_id, t.type_name, c."Severity" AS severity, c."NutrientId" AS nutrient_id,
           btrim(c."IngredientPattern") AS pattern,
           CASE WHEN c."NutrientId" IS NOT NULL THEN 'nutrient'
                WHEN left(btrim(c."IngredientPattern"), 2) = '\m' THEN 'word'
                ELSE 'like' END AS kind,
           coalesce(lower(btrim(n."Name")) = 'alcohol', false) AS alcohol,
           c."MaxAmountPerServing" IS NULL AS uncapped
    FROM types t
    JOIN plan."RestrictionCriterion" c ON c."RestrictionTypeId" = t.ref_id
    LEFT JOIN nutrient."Nutrient" n ON n."Id" = c."NutrientId"
    WHERE NOT c."IsDeleted"
      AND (c."NutrientId" IS NOT NULL
           OR (c."NutrientId" IS NULL AND btrim(coalesce(c."IngredientPattern", '')) <> ''))
),
crit AS (
    SELECT DISTINCT type_id, type_name, pattern, severity FROM all_crit WHERE kind = 'like'
),
word_crit AS (
    SELECT DISTINCT type_id, pattern FROM all_crit WHERE kind = 'word'
),
nut_crit AS (
    SELECT DISTINCT type_id, nutrient_id, alcohol, uncapped FROM all_crit WHERE kind = 'nutrient'
),
alcohol_types AS (
    SELECT DISTINCT type_id FROM nut_crit WHERE alcohol
),
pats AS (
    SELECT DISTINCT pattern FROM crit
),
rule_of AS (
    SELECT p.pattern, r.seed, r.term_rx, r.blank_rx, r.pattern IS NULL AS by_like,
           r.pattern IS NULL AND (btrim(p.pattern) ~ '^%%|%%$|[_\\]' OR strpos(btrim(btrim(p.pattern), '%'), '%') > 0) AS substring
    FROM pats p
    LEFT JOIN rule_pats r ON r.pattern = lower(p.pattern)
),
word_pats AS (
    SELECT DISTINCT pattern FROM word_crit
),
by_name AS (
    SELECT p.pattern, i."Id" AS food
    FROM pats p
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ILIKE p.pattern
),
by_alias AS (
    SELECT p.pattern, a."IngredientId" AS food,
           coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                    OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS trusted
    FROM pats p
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ILIKE p.pattern
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
),
by_category AS (
    SELECT p.pattern, i."Id" AS food
    FROM pats p
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."FdcCategoryId" = 10
    WHERE lower(p.pattern) = '%pork%'
),
stored AS (
    SELECT DISTINCT n.type_id, x."IngredientId" AS food
    FROM nut_crit n
    JOIN nutrient."IngredientNutrient" x ON x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted"
),
word_name_hits AS (
    SELECT DISTINCT c.pattern, i."Id" AS food
    FROM word_pats c
    CROSS JOIN consts k
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ~* c.pattern
    WHERE NOT (i."Name" ~* k.drink AND i."Name" ~* k.not_drink)
),
word_name AS (
    SELECT DISTINCT c.type_id, h.food
    FROM word_crit c
    JOIN word_name_hits h ON h.pattern = c.pattern
),
word_alias_hits AS (
    SELECT DISTINCT c.pattern, a."IngredientId" AS food,
           a."AliasName" !~* k.drink AS counts_old,
           a."AliasName" !~* k.drink
               AND coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                            OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS counts_new
    FROM word_pats c
    CROSS JOIN consts k
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ~* c.pattern
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
),
word_alias AS (
    SELECT DISTINCT c.type_id, h.food, h.counts_old, h.counts_new
    FROM word_crit c
    JOIN word_alias_hits h ON h.pattern = c.pattern
),
drink_foods AS (
    SELECT i."Id" AS food, i."FdcCategoryId", i."Name"
    FROM recipe."Ingredient" i
    CROSS JOIN consts k
    WHERE NOT i."IsDeleted" AND i."Name" ~* k.drink AND i."Name" !~* k.not_drink
),
nutrient_foods AS (
    SELECT DISTINCT n.type_id, i."Id" AS food
    FROM nut_crit n
    JOIN nutrient."IngredientNutrient" x ON x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted" AND x."Amount" > 0.5
    JOIN recipe."Ingredient" i ON i."Id" = x."IngredientId" AND NOT i."IsDeleted"
    WHERE n.uncapped
      AND (NOT n.alcohol
       OR (i."FdcCategoryId" IS DISTINCT FROM 2 AND i."Name" !~* '\m(?:extracts?|essences?|flavou?r\w*)\M'))
    UNION
    SELECT n.type_id, d.food
    FROM nut_crit n
    JOIN drink_foods d ON true
    WHERE n.alcohol
      AND NOT EXISTS (SELECT 1 FROM nutrient."IngredientNutrient" x WHERE x."IngredientId" = d.food AND x."NutrientId" = n.nutrient_id AND NOT x."IsDeleted")
),
other_old AS (
    SELECT w.type_id, w.food FROM word_name w WHERE NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE w.counts_old AND NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT type_id, food FROM nutrient_foods
),
other_new AS (
    SELECT w.type_id, w.food FROM word_name w WHERE NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE w.counts_new AND NOT EXISTS (SELECT 1 FROM stored s WHERE s.type_id = w.type_id AND s.food = w.food)
    UNION
    SELECT type_id, food FROM nutrient_foods
),
old_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name UNION SELECT pattern, food FROM by_alias) f ON f.pattern = c.pattern
    UNION
    SELECT type_id, food FROM other_old
),
new_foods AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM by_name
          UNION SELECT pattern, food FROM by_alias WHERE trusted
          UNION SELECT pattern, food FROM by_category) f ON f.pattern = c.pattern
    UNION
    SELECT type_id, food FROM other_new
),
alias_only AS (
    SELECT DISTINCT c.type_id, a.food
    FROM crit c
    JOIN by_alias a ON a.pattern = c.pattern AND NOT a.trusted
    UNION
    SELECT w.type_id, w.food FROM word_alias w WHERE NOT w.counts_new
    EXCEPT
    SELECT type_id, food FROM new_foods
),
live AS (
    SELECT ri."RecipeId", ri."IngredientId", ri."RawLine", s."Name" = 'Approved' AS approved
    FROM recipe."RecipeIngredient" ri
    JOIN recipe."Recipe" r ON r."Id" = ri."RecipeId" AND NOT r."IsDeleted"
    LEFT JOIN reference."Reference" s ON s."Id" = r."CurationStatusId"
    WHERE NOT ri."IsDeleted"
),
drink_recipes AS (
    SELECT DISTINCT l."RecipeId", l.approved
    FROM live l
    CROSS JOIN consts k
    WHERE EXISTS (SELECT 1 FROM alcohol_types)
      AND l."RawLine" ~* k.drink AND l."RawLine" !~* k.not_drink
),
drink_lines AS (
    SELECT t.type_id, d."RecipeId", d.approved
    FROM alcohol_types t
    CROSS JOIN drink_recipes d
),
old_recipes AS (
    SELECT DISTINCT o.type_id, l."RecipeId", l.approved
    FROM old_foods o JOIN live l ON l."IngredientId" = o.food
    UNION
    SELECT * FROM drink_lines
),
new_by_food AS (
    SELECT DISTINCT n.type_id, l."RecipeId", l.approved
    FROM new_foods n JOIN live l ON l."IngredientId" = n.food
),
touched AS (
    SELECT DISTINCT a.type_id, l."RecipeId"
    FROM alias_only a JOIN live l ON l."IngredientId" = a.food
),
touched_lines AS (
    SELECT DISTINCT l."RecipeId", l."RawLine", l.approved
    FROM live l
    WHERE l."RecipeId" IN (SELECT "RecipeId" FROM touched)
),
like_hits AS (
    SELECT DISTINCT p.pattern, tl."RecipeId", tl.approved
    FROM pats p
    JOIN touched_lines tl ON tl."RawLine" ILIKE p.pattern
),
word_hits AS (
    SELECT DISTINCT c.pattern, tl."RecipeId", tl.approved
    FROM word_pats c
    CROSS JOIN consts k
    JOIN touched_lines tl ON tl."RawLine" ~* c.pattern
    WHERE NOT (tl."RawLine" ~* k.drink AND tl."RawLine" ~* k.not_drink)
),
by_line AS (
    SELECT DISTINCT t.type_id, h."RecipeId", h.approved
    FROM touched t
    JOIN crit c ON c.type_id = t.type_id
    JOIN like_hits h ON h.pattern = c.pattern AND h."RecipeId" = t."RecipeId"
    UNION
    SELECT DISTINCT t.type_id, h."RecipeId", h.approved
    FROM touched t
    JOIN word_crit c ON c.type_id = t.type_id
    JOIN word_hits h ON h.pattern = c.pattern AND h."RecipeId" = t."RecipeId"
),
new_recipes AS (
    SELECT * FROM new_by_food UNION SELECT * FROM by_line UNION SELECT * FROM drink_lines
),
lost AS (
    SELECT * FROM old_recipes EXCEPT SELECT * FROM new_recipes
),
line_pat AS (
    SELECT p.pattern, l."RecipeId", l."IngredientId", l."RawLine", l.approved
    FROM pats p
    JOIN live l ON l."RawLine" ILIKE p.pattern
),
line_word_pats AS (
    SELECT DISTINCT pattern FROM word_crit WHERE type_id NOT IN (SELECT type_id FROM alcohol_types)
),
line_word AS (
    SELECT c.pattern, l."RecipeId", l."IngredientId", l."RawLine", l.approved
    FROM line_word_pats c
    CROSS JOIN consts k
    JOIN live l ON l."RawLine" ~* c.pattern
    WHERE NOT (l."RawLine" ~* k.drink AND l."RawLine" ~* k.not_drink)
),
any_line_hits AS (
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM crit c
    JOIN line_pat x ON x.pattern = c.pattern
    UNION
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM word_crit c
    JOIN line_word x ON x.pattern = c.pattern
    WHERE c.type_id NOT IN (SELECT type_id FROM alcohol_types)
),
any_line AS (
    SELECT DISTINCT type_id, "RecipeId", approved FROM any_line_hits
),
by_any_line AS (
    SELECT * FROM any_line EXCEPT SELECT * FROM new_recipes
),
gained AS (
    SELECT * FROM new_recipes EXCEPT SELECT * FROM old_recipes
),
rule_name AS (
    SELECT o.pattern, i."Id" AS food
    FROM rule_of o
    JOIN recipe."Ingredient" i ON NOT i."IsDeleted" AND i."Name" ILIKE CASE WHEN o.by_like THEN o.pattern ELSE o.seed END
    WHERE o.by_like
       OR (i."Name" ~* o.term_rx AND (o.blank_rx IS NULL OR regexp_replace(i."Name", o.blank_rx, ' ', 'gi') ~* o.term_rx))
),
rule_alias AS (
    SELECT o.pattern, a."IngredientId" AS food,
           coalesce(a."SourceContext" LIKE 'fdc:%' OR a."SourceContext" LIKE 'admin:%' OR a."SourceContext" LIKE 'deterministic:%'
                    OR a."SourceContext" LIKE 'retired:%' OR a."SourceContext" LIKE 'link:deterministic:%', false) AS trusted
    FROM rule_of o
    JOIN recipe."IngredientAlias" a ON NOT a."IsDeleted" AND a."AliasName" ILIKE CASE WHEN o.by_like THEN o.pattern ELSE o.seed END
    JOIN recipe."Ingredient" i ON i."Id" = a."IngredientId" AND NOT i."IsDeleted"
    WHERE o.by_like
       OR (a."AliasName" ~* o.term_rx AND (o.blank_rx IS NULL OR regexp_replace(a."AliasName", o.blank_rx, ' ', 'gi') ~* o.term_rx))
),
new_foods_rule AS (
    SELECT DISTINCT c.type_id, f.food
    FROM crit c
    JOIN (SELECT pattern, food FROM rule_name
          UNION SELECT pattern, food FROM rule_alias WHERE trusted
          UNION SELECT pattern, food FROM by_category) f ON f.pattern = c.pattern
    UNION
    SELECT type_id, food FROM other_new
),
new_by_food_rule AS (
    SELECT DISTINCT n.type_id, l."RecipeId", l.approved
    FROM new_foods_rule n JOIN live l ON l."IngredientId" = n.food
),
line_rule AS (
    SELECT o.pattern, l."RecipeId", l."IngredientId", l."RawLine", l.approved
    FROM rule_of o
    JOIN live l ON l."RawLine" ILIKE CASE WHEN o.by_like THEN o.pattern ELSE o.seed END
    WHERE o.by_like
       OR (l."RawLine" ~* o.term_rx AND (o.blank_rx IS NULL OR regexp_replace(l."RawLine", o.blank_rx, ' ', 'gi') ~* o.term_rx))
),
any_line_rule_hits AS (
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM crit c
    JOIN line_rule x ON x.pattern = c.pattern
    UNION
    SELECT c.type_id, x."RecipeId", x."IngredientId", x."RawLine", x.approved
    FROM word_crit c
    JOIN line_word x ON x.pattern = c.pattern
    WHERE c.type_id NOT IN (SELECT type_id FROM alcohol_types)
),
any_line_rule AS (
    SELECT DISTINCT type_id, "RecipeId", approved FROM any_line_rule_hits
),
by_line_word AS (
    SELECT DISTINCT t.type_id, h."RecipeId", h.approved
    FROM touched t
    JOIN word_crit c ON c.type_id = t.type_id
    JOIN word_hits h ON h.pattern = c.pattern AND h."RecipeId" = t."RecipeId"
),
held_rule AS (
    SELECT * FROM new_by_food_rule UNION SELECT * FROM by_line_word UNION SELECT * FROM drink_lines
),
total_crude AS (
    SELECT * FROM new_recipes UNION SELECT * FROM any_line
),
total_rule AS (
    SELECT * FROM held_rule UNION SELECT * FROM any_line_rule
),
rule_dropped AS (
    SELECT * FROM total_crude EXCEPT SELECT * FROM total_rule
),
rule_added AS (
    SELECT * FROM total_rule EXCEPT SELECT * FROM total_crude
),
by_rule_line AS (
    SELECT * FROM any_line_rule EXCEPT SELECT * FROM held_rule
),
rule_foods_dropped AS (
    SELECT type_id, food FROM new_foods EXCEPT SELECT type_id, food FROM new_foods_rule
),
substring_pats AS (
    SELECT DISTINCT c.type_id, c.pattern FROM crit c JOIN rule_of o ON o.pattern = c.pattern WHERE o.substring
),
unread_pats AS (
    SELECT DISTINCT c.type_id, c.pattern FROM crit c JOIN rule_of o ON o.pattern = c.pattern WHERE o.by_like AND NOT o.substring
),
counts AS (
    SELECT type_id, 'foods_before' AS k, count(*) AS n FROM old_foods GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_after', count(*) FROM new_foods GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_alias_only', count(*) FROM alias_only GROUP BY type_id
    UNION ALL SELECT type_id, 'recipes_before', count(*) FROM old_recipes GROUP BY type_id
    UNION ALL SELECT type_id, 'recipes_after', count(*) FROM new_recipes GROUP BY type_id
    UNION ALL SELECT type_id, 'kept_by_line', count(*) FROM by_line GROUP BY type_id
    UNION ALL SELECT type_id, 'lost', count(*) FROM lost GROUP BY type_id
    UNION ALL SELECT type_id, 'lost_approved', count(*) FROM lost WHERE approved GROUP BY type_id
    UNION ALL SELECT type_id, 'gained', count(*) FROM gained GROUP BY type_id
    UNION ALL SELECT type_id, 'kept_by_any_line', count(*) FROM any_line GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_any_line', count(*) FROM by_any_line GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_any_line_approved', count(*) FROM by_any_line WHERE approved GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_rule', count(*) FROM new_foods_rule GROUP BY type_id
    UNION ALL SELECT type_id, 'foods_rule_dropped', count(*) FROM rule_foods_dropped GROUP BY type_id
    UNION ALL SELECT type_id, 'total_crude', count(*) FROM total_crude GROUP BY type_id
    UNION ALL SELECT type_id, 'total_rule', count(*) FROM total_rule GROUP BY type_id
    UNION ALL SELECT type_id, 'rule_dropped', count(*) FROM rule_dropped GROUP BY type_id
    UNION ALL SELECT type_id, 'rule_dropped_approved', count(*) FROM rule_dropped WHERE approved GROUP BY type_id
    UNION ALL SELECT type_id, 'rule_added', count(*) FROM rule_added GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_rule_line', count(*) FROM by_rule_line GROUP BY type_id
    UNION ALL SELECT type_id, 'gained_by_rule_line_approved', count(*) FROM by_rule_line WHERE approved GROUP BY type_id
    UNION ALL SELECT type_id, 'substring_patterns', count(*) FROM substring_pats GROUP BY type_id
    UNION ALL SELECT type_id, 'unread_patterns', count(*) FROM unread_pats GROUP BY type_id
),
severities AS (
    SELECT type_id, max(severity) AS severity FROM all_crit GROUP BY type_id
),
summary AS (
    SELECT g.type_id, g.type_name, g.ref_ids, v.severity,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_before'), 0) AS foods_before,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_after'), 0) AS foods_after,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_alias_only'), 0) AS foods_alias_only,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'recipes_before'), 0) AS recipes_before,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'recipes_after'), 0) AS recipes_after,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'kept_by_line'), 0) AS kept_by_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'lost'), 0) AS lost,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'lost_approved'), 0) AS lost_approved,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained'), 0) AS gained,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'kept_by_any_line'), 0) AS kept_by_any_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_any_line'), 0) AS gained_by_any_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_any_line_approved'), 0) AS gained_by_any_line_approved,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_rule'), 0) AS foods_rule,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'foods_rule_dropped'), 0) AS foods_rule_dropped,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'total_crude'), 0) AS total_crude,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'total_rule'), 0) AS total_rule,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'rule_dropped'), 0) AS rule_dropped,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'rule_dropped_approved'), 0) AS rule_dropped_approved,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'rule_added'), 0) AS rule_added,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_rule_line'), 0) AS gained_by_rule_line,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'gained_by_rule_line_approved'), 0) AS gained_by_rule_line_approved,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'substring_patterns'), 0) AS substring_patterns,
           coalesce(sum(c.n) FILTER (WHERE c.k = 'unread_patterns'), 0) AS unread_patterns
    FROM type_groups g
    JOIN severities v ON v.type_id = g.type_id
    LEFT JOIN counts c ON c.type_id = g.type_id
    GROUP BY g.type_id, g.type_name, g.ref_ids, v.severity
),
lost_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT l."RawLine", ' | ') AS lines_on_alias_only_foods,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM lost x
    JOIN summary s ON s.type_id = x.type_id
    JOIN live l ON l."RecipeId" = x."RecipeId"
    JOIN alias_only a ON a.type_id = x.type_id AND a.food = l."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
),
line_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT h."RawLine" || ' [on ' || coalesce(i."Name", '#' || h."IngredientId") || ']', ' | ') AS lines,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM by_any_line x
    JOIN summary s ON s.type_id = x.type_id
    JOIN any_line_hits h ON h.type_id = x.type_id AND h."RecipeId" = x."RecipeId"
    LEFT JOIN recipe."Ingredient" i ON i."Id" = h."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
),
rule_drop_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT h."RawLine" || ' [on ' || coalesce(i."Name", '#' || h."IngredientId") || ']', ' | ') AS lines,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM rule_dropped x
    JOIN summary s ON s.type_id = x.type_id
    JOIN (SELECT type_id, "RecipeId", "IngredientId", "RawLine" FROM any_line_hits
          UNION
          SELECT d.type_id, l."RecipeId", l."IngredientId", l."RawLine" FROM rule_foods_dropped d JOIN live l ON l."IngredientId" = d.food) h
      ON h.type_id = x.type_id AND h."RecipeId" = x."RecipeId"
    LEFT JOIN recipe."Ingredient" i ON i."Id" = h."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
),
rule_line_sample AS (
    SELECT s.type_name, s.severity, x."RecipeId",
           string_agg(DISTINCT h."RawLine" || ' [on ' || coalesce(i."Name", '#' || h."IngredientId") || ']', ' | ') AS lines,
           row_number() OVER (PARTITION BY s.type_id ORDER BY x.approved DESC, md5(x."RecipeId"::text || :'seed')) AS n
    FROM by_rule_line x
    JOIN summary s ON s.type_id = x.type_id
    JOIN any_line_rule_hits h ON h.type_id = x.type_id AND h."RecipeId" = x."RecipeId"
    LEFT JOIN recipe."Ingredient" i ON i."Id" = h."IngredientId"
    GROUP BY s.type_id, s.type_name, s.severity, x."RecipeId", x.approved
),
rule_food_sample AS (
    SELECT s.type_name, s.severity, d.food, i."Name" AS food_name,
           row_number() OVER (PARTITION BY s.type_id ORDER BY md5(d.food::text || :'seed')) AS n
    FROM rule_foods_dropped d
    JOIN summary s ON s.type_id = d.type_id
    JOIN recipe."Ingredient" i ON i."Id" = d.food
)
SELECT 'summary' AS kind, type_name, severity::text,
       concat_ws(' ', 'ids', ref_ids, 'foods', foods_before, '->', foods_after, '(alias only', foods_alias_only || ')',
                 'recipes', recipes_before, '->', recipes_after, '(kept by a line', kept_by_line || ')',
                 'lost', lost, '(approved', lost_approved || ')', 'gained', gained,
                 'any-line', kept_by_any_line, 'gained by any line', gained_by_any_line, '(approved', gained_by_any_line_approved || ')',
                 'word rule: foods', foods_rule, '(dropped', foods_rule_dropped || ')', 'recipes', total_crude, '->', total_rule,
                 'dropped', rule_dropped, '(approved', rule_dropped_approved || ')', 'added', rule_added,
                 'gained by line text only', gained_by_rule_line, '(approved', gained_by_rule_line_approved || ')',
                 'patterns read as substring', substring_patterns, 'patterns the table lacks (read by ILIKE)', unread_patterns) AS detail
FROM summary
UNION ALL
SELECT 'lost', type_name, severity::text, "RecipeId" || ': ' || lines_on_alias_only_foods
FROM lost_sample
WHERE n <= :sample AND severity >= 4
UNION ALL
SELECT 'line', type_name, severity::text, "RecipeId" || ': ' || lines
FROM line_sample
WHERE n <= :sample AND severity >= 4
UNION ALL
SELECT 'rule-drop', type_name, severity::text, "RecipeId" || ': ' || lines
FROM rule_drop_sample
WHERE n <= :sample
UNION ALL
SELECT 'rule-line', type_name, severity::text, "RecipeId" || ': ' || lines
FROM rule_line_sample
WHERE n <= :sample
UNION ALL
SELECT 'rule-food', type_name, severity::text, food || ': ' || food_name
FROM rule_food_sample
WHERE n <= :sample
ORDER BY 1 DESC, 2, 4;
ROLLBACK;
