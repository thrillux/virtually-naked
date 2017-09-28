﻿public class FigureDefinition {
	public static FigureDefinition Load(IArchiveDirectory figureDir, FigureDefinition parent) {
		var channelSystemRecipe = Persistance.Load<ChannelSystemRecipe>(figureDir.File("channel-system-recipe.dat"));
		var channelSystem = channelSystemRecipe.Bake(parent?.ChannelSystem);
		
		BoneSystem boneSystem;
		if (parent != null) {
			boneSystem = parent.BoneSystem;
		} else {
			var boneSystemRecipe = Persistance.Load<BoneSystemRecipe>(figureDir.File("bone-system-recipe.dat"));
			boneSystem = boneSystemRecipe.Bake(channelSystem.ChannelsByName);
		}

		return new FigureDefinition(channelSystem, boneSystem);
	}
	
	public ChannelSystem ChannelSystem { get; }
	public BoneSystem BoneSystem { get; }

	public FigureDefinition(ChannelSystem channelSystem, BoneSystem boneSystem) {
		ChannelSystem = channelSystem;
		BoneSystem = boneSystem;
	}
}