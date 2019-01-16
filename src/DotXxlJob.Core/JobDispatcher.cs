using System;
using System.Collections.Concurrent;
using DotXxlJob.Core.Model;
using Microsoft.Extensions.Logging;

namespace DotXxlJob.Core
{
    /// <summary>
    /// 负责实际的JOB轮询
    /// </summary>
    public class JobDispatcher
    {
        private readonly TaskExecutorFactory _executorFactory;
        private readonly CallbackTaskQueue _callbackTaskQueue;
       
        private readonly ConcurrentDictionary<int,JobQueue> RUNNING_QUEUE = new ConcurrentDictionary<int, JobQueue>();


        private readonly ILogger<JobQueue> _jobQueueLogger;
        private readonly ILogger<JobDispatcher> _logger;
        public JobDispatcher(
            TaskExecutorFactory executorFactory,
            CallbackTaskQueue callbackTaskQueue,
            ILoggerFactory loggerFactory
            )
        {
            this. _executorFactory = executorFactory;
            this. _callbackTaskQueue = callbackTaskQueue;
          

            this._jobQueueLogger =  loggerFactory.CreateLogger<JobQueue>();
            this._logger =  loggerFactory.CreateLogger<JobDispatcher>();
        }
    
     
        /// <summary>
        /// 尝试移除JobTask
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        public bool TryRemoveJobTask(int jobId)
        {
            if (RUNNING_QUEUE.TryGetValue(jobId, out var jobQueue))
            {
                jobQueue.Stop();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 执行队列，并快速返回结果
        /// </summary>
        /// <param name="triggerParam"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public ReturnT Execute(TriggerParam triggerParam)
        {

            var executor = this._executorFactory.GetTaskExecutor(triggerParam.GlueType);
            if (executor == null)
            {
                return ReturnT.Failed($"glueType[{triggerParam.GlueType}] is not supported ");
            }
            
            // 1. 根据JobId 获取 TaskQueue; 用于判断是否有正在执行的任务
            if (RUNNING_QUEUE.TryGetValue(triggerParam.JobId, out var taskQueue))
            {
                if (taskQueue.Executor != executor) //任务执行器变更
                {
                    return ChangeJobQueue(triggerParam, executor);
                }
            }

            if (taskQueue != null) //旧任务还在执行，判断执行策略
            {
                //丢弃后续的
                if (Constants.ExecutorBlockStrategy.DISCARD_LATER == triggerParam.ExecutorBlockStrategy)
                {
                     return ReturnT.Failed($"block strategy effect：{triggerParam.ExecutorBlockStrategy}");
                }
                //覆盖较早的
                if (Constants.ExecutorBlockStrategy.COVER_EARLY == triggerParam.ExecutorBlockStrategy)
                {
                    return taskQueue.Replace(triggerParam);
                }
            }
            
            return PushJobQueue(triggerParam, executor);
           
        }


        /// <summary>
        /// 等待检查
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        public ReturnT IdleBeat(int jobId)
        {
            return RUNNING_QUEUE.ContainsKey(jobId) ? 
                new ReturnT(ReturnT.FAIL_CODE, "job thread is running or has trigger queue.") 
                : ReturnT.SUCCESS;
        }
        
      
        private ReturnT PushJobQueue(TriggerParam triggerParam, ITaskExecutor executor)
        { 
            JobQueue jobQueue = new JobQueue ( executor, this._callbackTaskQueue,this._jobQueueLogger);
            if (RUNNING_QUEUE.TryAdd(triggerParam.JobId, jobQueue))
            {
                jobQueue.Push(triggerParam);
            }
            return ReturnT.Failed("add running queue executor error");
        }
        
        private ReturnT ChangeJobQueue(TriggerParam triggerParam, ITaskExecutor executor)
        {
            
            JobQueue jobQueue = new JobQueue ( executor, this._callbackTaskQueue,this._jobQueueLogger);
            if (RUNNING_QUEUE.TryUpdate(triggerParam.JobId, jobQueue, null))
            {
                return jobQueue.Push(triggerParam);
            }
            return ReturnT.Failed(" replace running queue executor error");
        }

    }
}