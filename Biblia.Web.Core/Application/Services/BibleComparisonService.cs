using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class BibleComparisonService(IBibleRepository repository,ILogger<BibleComparisonService> logger):IBibleComparisonService
{
    public async Task<IReadOnlyList<BibleComparisonItem>> CompareAsync(int bookReferenceId,int chapter,int verseStart,int verseEnd,IReadOnlyList<string> versionCodes,CancellationToken token=default)
    {
        if(versionCodes.Count<2)throw new ArgumentException("Selecione ao menos duas versões.",nameof(versionCodes));
        var result=new List<BibleComparisonItem>();
        foreach(var code in versionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var passage=await repository.GetPassageAsync(code,bookReferenceId,chapter,verseStart,verseEnd,token);
                var status=passage.Verses.Count==0?BibleComparisonStatus.Missing:passage.HasAmbiguities?BibleComparisonStatus.Ambiguous:BibleComparisonStatus.Available;
                var message=status switch{BibleComparisonStatus.Missing=>"Referência ausente nesta versão.",BibleComparisonStatus.Ambiguous=>"A versão contém coordenadas duplicadas; nenhum texto foi escolhido silenciosamente.",_=>null};
                result.Add(new(code.ToUpperInvariant(),status,passage.Verses,message));
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,"Falha ao comparar a versão {VersionCode}.",code);result.Add(new(code.ToUpperInvariant(),BibleComparisonStatus.Error,[],ex.Message));
            }
        }
        return result;
    }
}
