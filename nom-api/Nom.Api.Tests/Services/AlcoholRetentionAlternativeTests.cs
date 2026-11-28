using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The retention reader of <c>alcohol-retention/11</c> (staging checkpoint 9, U12): times offered as alternatives are
    /// never added up (18 and 30 minutes read as 48, 35 %, where either alone applies), the shorter counts as an upper
    /// bound; and a part of the method that every part of the dish goes on to is read on to its heat (a preparation added
    /// back into the bowl in its own sentence, scraps of a dough put back in the fridge, a dough frozen for later use, a
    /// mould filled with a custard and boiled). Step and line texts are written for the rule.
    /// </summary>
    public class AlcoholRetentionAlternativeTests
    {
        [Theory]
        [InlineData("Beat the butter and sugar, then the eggs and vanilla; stir in the flour.|Spread the dough in the pan.|Bake for 20 minutes until just set.|For a firmer bar, bake for 30 minutes.", "1 tsp vanilla", "up-to:bake:20:choice", 0.45)]
        [InlineData("Cream the butter and sugar and add the brandy.|Beat in the eggs and the flour.|Put the mixture in a buttered pan.|Bake it two hours in a moderate oven.|If the cake is thin, one hour will be enough.", "1 glass of brandy", "up-to:bake:60:choice", 0.25)]
        [InlineData("Stir the rum into the batter and bake for 25 minutes.|For a darker crust, bake 5 minutes more.", "2 tbsp rum", "up-to:bake:25:choice", 0.45)]
        [InlineData("Stir the rum into the batter.|Bake for 30 minutes.|For a softer cookie, bake for 15 minutes.", "2 tbsp rum", "up-to:bake:15:choice", 0.45)]
        [InlineData("Stir the bourbon into the fruit and spread it in the dish.|Bake for 30 minutes.|For a crisper top, broil it 1 minute.", "2 tbsp bourbon", "up-to:bake:30:choice", 0.35)]
        [InlineData("Stir the vanilla into the batter and spread it in the pan.|Bake for 35-40 minutes.|For gooier brownies, bake 30-35 minutes.", "1 tsp vanilla", "up-to:bake:30:choice", 0.35)]
        public void Times_offered_as_alternatives_are_never_added_and_the_shorter_counts_as_an_upper_bound(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
            reading.UpTo.Should().BeTrue();
        }

        [Theory]
        [InlineData("Beat the butter with the brandy and half the flour.|Whisk the whites stiff and fold them gently into the rest of the flour.|Bake for 50 minutes.", "2 tbsp brandy", "timed:bake:50", 0.35)]
        [InlineData("In a bowl, whisk the eggs, sugar and vanilla.|Chop the apples and stir them into the bowl of oats.|Pour into a baking dish and bake for 45 minutes.", "1 tsp vanilla", "timed:bake:45", 0.35)]
        [InlineData("Beat the butter, sugar, egg and vanilla, then mix in the flour.|Halve the dough and refrigerate it for 1 hour.|Roll out one half, stamp out rounds and put the trimmings back in the fridge.|Set the rounds on a lined tray and bake for 10 minutes.|Do the same with the rest of the dough.", "1 tsp vanilla", "up-to:bake:10", 0.45)]
        [InlineData("Beat the butter, sugar and vanilla; mix in the flour.|Roll it into balls and chill them for an hour.|Alternatively, freeze the balls for later use.|Bake on a lined sheet for 12 minutes.", "1 tsp vanilla", "up-to:bake:12", 0.45)]
        [InlineData("Soak the sponge fingers in the brandy and lay them in a mould.|Make a custard of the milk and eggs.|Fill up the mould with the custard.|Tie a cloth over it and boil it for an hour.", "2 tbsp brandy", "up-to:simmer:60:marinade", 0.25)]
        public void A_part_every_part_of_the_dish_goes_on_with_is_read_to_its_heat(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
        }

        [Theory]
        [InlineData("Whip the cream with the brandy and sugar.|Beat the eggs and add them to the batter.|Bake the cake for 30 minutes.", "2 tbsp brandy", "up-to:no-heat::parts", 1.0)]
        [InlineData("Beat the eggs with the rum.|Whisk the cream until stiff and add it to the soup.|Simmer the soup for 30 minutes.", "2 tbsp rum", "up-to:no-heat::parts", 1.0)]
        public void A_part_that_goes_into_something_else_still_ends_the_run(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
        }

        [Theory]
        [InlineData("Mix the cream, milk, sugar, eggs and vanilla.|Stir thoroughly but do not cook.|Freeze.", "1 tsp vanilla", "no-heat", 1.0)]
        [InlineData("Heat the milk but do not let it boil, then add the brandy.|Serve.", "2 tbsp brandy", "hot-liquid", 0.85)]
        public void Do_not_cook_inside_a_sentence_is_no_heat(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
        }

        [Fact]
        public void The_reader_is_version_eleven()
        {
            AlcoholRetention.Version.Should().Be("alcohol-retention/11");
        }
    }
}
